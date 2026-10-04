using ContentDB.Core.Abstractions;
using ContentDB.Core.Configuration;
using ContentDB.Core.Entities;
using ContentDB.Infrastructure.Caching;
using ContentDB.Infrastructure.Data;
using ContentDB.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ContentDB.Infrastructure.Tests;

public sealed class CacheLifecycleTests
{
	private sealed class Clock : TimeProvider
	{
		public DateTimeOffset Now = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
		public override DateTimeOffset GetUtcNow() => Now;
	}
	private static readonly CachedApiResult Result = new(200, "{}", "application/json");
	private static UpstreamSiteOptions Source => new() { Id = "official", BaseUrl = "https://upstream.example" };

	[Fact]
	public void LruEvictsOnlyLeastRecentlyUsedAndExpiresAtBoundary()
	{
		var clock = new Clock();
		var cache = new MetadataResponseCache(clock, 2);
		cache.Remember("a", Result, clock.Now.AddMinutes(5));
		cache.Remember("b", Result, clock.Now.AddSeconds(10));
		Assert.True(cache.TryGet("a", out _));
		cache.Remember("c", Result, clock.Now.AddMinutes(5));
		Assert.False(cache.TryGet("b", out _));
		Assert.True(cache.TryGet("a", out _));
		Assert.True(cache.TryGet("c", out _));
		clock.Now += TimeSpan.FromSeconds(60);
		Assert.False(cache.TryGet("a", out _));
		Assert.False(cache.TryGet("c", out _));
		cache.Remember("short", Result, clock.Now.AddSeconds(3));
		clock.Now += TimeSpan.FromSeconds(3);
		Assert.False(cache.TryGet("short", out _));
	}

	[Fact]
	public void CacheIsHostLocalAndNeverRemembersErrorsOrExpiredResults()
	{
		var clock = new Clock();
		var a = new MetadataResponseCache(clock);
		var b = new MetadataResponseCache(clock);
		a.Remember("ok", Result, clock.Now.AddSeconds(10));
		Assert.False(b.TryGet("ok", out _));
		a.Remember("error", Result with { StatusCode = 503 }, clock.Now.AddSeconds(10));
		a.Remember("expired", Result, clock.Now);
		Assert.False(a.TryGet("error", out _));
		Assert.False(a.TryGet("expired", out _));
	}

	[Fact]
	public async Task KeyedLocksSerializeAndReclaimAllEntries()
	{
		var gates = new KeyedAsyncLock();
		int active = 0, maximum = 0;
		await Task.WhenAll(Enumerable.Range(0, 100).Select(async _ =>
		{
			using var lease = await gates.AcquireAsync("same");
			maximum = Math.Max(maximum, ++active);
			await Task.Yield();
			active--;
		}));
		Assert.Equal(1, maximum);
		for (var i = 0; i < 2000; i++)
		{
			using var lease = await gates.AcquireAsync(i.ToString());
		}
		Assert.Equal(0, gates.EntryCount);
	}

	[Fact]
	public async Task CancelledWaiterDoesNotRemoveActiveGateAndReleaseIsIdempotent()
	{
		var gates = new KeyedAsyncLock();
		var holder = await gates.AcquireAsync("a");
		using var cts = new CancellationTokenSource();
		var waiting = gates.AcquireAsync("a", cts.Token).AsTask();
		cts.Cancel();
		await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await waiting);
		Assert.Equal(1, gates.EntryCount);
		using (await gates.AcquireAsync("b")) { } // unrelated keys make progress
		holder.Dispose();
		holder.Dispose();
		Assert.Equal(0, gates.EntryCount);
	}

	[Fact]
	public void CacheIdentitySeparatesRepresentationsAndSourceConfiguration()
	{
		string Key(UpstreamSiteOptions source, bool rewrite = true, string publicUrl = "https://mirror.example",
			string? lang = null, string? ua = null, string path = "/api/packages/")
			=> MetadataCacheService.BuildKey(source, path, lang, ua, rewrite, publicUrl);
		var key = Key(Source);
		Assert.NotEqual(key, Key(Source, false));
		Assert.NotEqual(key, Key(Source, publicUrl: "https://other-mirror.example"));
		Assert.NotEqual(key, Key(new() { Id = Source.Id, BaseUrl = "https://other.example" }));
		Assert.NotEqual(key, Key(Source, lang: "zh"));
		Assert.NotEqual(key, Key(Source, ua: "Luanti"));
		Assert.NotEqual(key, Key(Source, path: "/api/packages/?q=a|lang=b"));
		Assert.True(Key(Source, path: new string('x', 2000)).Length < 512);
	}

	private sealed class Upstream(Func<CancellationToken, Task<UpstreamJsonResponse>> fetch) : IUpstreamClient
	{
		public int Calls;
		public Task<UpstreamJsonResponse> GetJsonAsync(string baseUrl, string path, string? acceptLanguage = null,
			string? userAgent = null, CancellationToken ct = default, IReadOnlyDictionary<string, string>? extraHeaders = null)
		{
			Interlocked.Increment(ref Calls);
			return fetch(ct);
		}
		public Task<UpstreamFileResponse> GetFileAsync(string baseUrl, string url, string? userAgent = null,
			CancellationToken ct = default) => throw new NotSupportedException();
	}

	private static MirrorDbContext Db(string name) => new(new DbContextOptionsBuilder<MirrorDbContext>()
		.UseInMemoryDatabase(name).Options);
	private static MetadataCacheService Service(MirrorDbContext db, Upstream upstream, Clock clock,
		MetadataResponseCache cache, KeyedAsyncLock gates) => new(db, upstream, Options.Create(new MirrorOptions()),
		NullLogger<MetadataCacheService>.Instance, cache, gates, clock);

	[Fact]
	public async Task ConcurrentMissesFetchOnceAcrossRequestScopes()
	{
		var clock = new Clock();
		var cache = new MetadataResponseCache(clock);
		var gates = new KeyedAsyncLock();
		var release = new TaskCompletionSource<UpstreamJsonResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
		var upstream = new Upstream(_ => release.Task);
		var name = Guid.NewGuid().ToString();
		var contexts = Enumerable.Range(0, 20).Select(_ => Db(name)).ToList();
		try
		{
			var pending = contexts.Select(db => Service(db, upstream, clock, cache, gates).GetOrFetchAsync(Source, "/api/packages/", 300)).ToArray();
			release.SetResult(new(200, "{}", "application/json"));
			await Task.WhenAll(pending);
			Assert.Equal(1, upstream.Calls);
			Assert.Equal(0, gates.EntryCount);
		}
		finally { foreach (var db in contexts) await db.DisposeAsync(); }
	}

	[Fact]
	public async Task SlowFetchGetsFullTtlAndCancellationPropagates()
	{
		var clock = new Clock();
		var cache = new MetadataResponseCache(clock);
		var gates = new KeyedAsyncLock();
		await using var db = Db(Guid.NewGuid().ToString());
		var upstream = new Upstream(_ =>
		{
			clock.Now += TimeSpan.FromMinutes(2);
			return Task.FromResult(new UpstreamJsonResponse(200, "{}", "application/json"));
		});
		await Service(db, upstream, clock, cache, gates).GetOrFetchAsync(Source, "/slow", 30);
		Assert.Equal(clock.Now.AddSeconds(30), (await db.CachedResponses.SingleAsync()).ExpiresAt);
		using var cts = new CancellationTokenSource();
		var cancelled = new Upstream(ct => { cts.Cancel(); ct.ThrowIfCancellationRequested(); throw new Exception(); });
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
			Service(db, cancelled, clock, cache, gates).GetOrFetchAsync(Source, "/cancel", 30, ct: cts.Token));
		Assert.Equal(0, gates.EntryCount);
	}

	[Fact]
	public async Task DatabaseOutageStillUsesHotCache()
	{
		var clock = new Clock();
		var cache = new MetadataResponseCache(clock);
		var db = Db(Guid.NewGuid().ToString());
		await db.DisposeAsync(); // deterministic read failure; no live database needed
		var upstream = new Upstream(_ => Task.FromResult(new UpstreamJsonResponse(200, "{}", "application/json")));
		var service = Service(db, upstream, clock, cache, new KeyedAsyncLock());
		Assert.Equal(200, (await service.GetOrFetchAsync(Source, "/outage", 30)).StatusCode);
		Assert.Equal(200, (await service.GetOrFetchAsync(Source, "/outage", 30)).StatusCode);
		Assert.Equal(1, upstream.Calls);
	}

	[Fact]
	public async Task RawAndRewrittenRequestsNeverShareResponseBodies()
	{
		var clock = new Clock();
		await using var db = Db(Guid.NewGuid().ToString());
		var upstream = new Upstream(_ => Task.FromResult(new UpstreamJsonResponse(200,
			"{\"url\":\"https://upstream.example/uploads/test.png\"}", "application/json")));
		var service = Service(db, upstream, clock, new MetadataResponseCache(clock), new KeyedAsyncLock());
		var raw = await service.GetOrFetchAsync(Source, "/mixed", 30, rewriteUrls: false);
		var rewritten = await service.GetOrFetchAsync(Source, "/mixed", 30);
		Assert.Contains("https://upstream.example", raw.Body);
		Assert.DoesNotContain("https://upstream.example", rewritten.Body);
		Assert.Equal(2, upstream.Calls);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task StaleFallbackHandlesHttpErrorsAndTransportErrors(bool transportError)
	{
		var clock = new Clock();
		await using var db = Db(Guid.NewGuid().ToString());
		db.CachedResponses.Add(new CachedResponse {
			CacheKey = MetadataCacheService.BuildKey(Source, "/stale", null, null, true, new MirrorOptions().PublicBaseUrl),
			Body = "old", ExpiresAt = clock.Now.AddSeconds(-1), FetchedAt = clock.Now.AddMinutes(-1) });
		await db.SaveChangesAsync();
		var upstream = new Upstream(_ => transportError
			? Task.FromException<UpstreamJsonResponse>(new HttpRequestException())
			: Task.FromResult(new UpstreamJsonResponse(503, "error", "text/plain")));
		var result = await Service(db, upstream, clock, new MetadataResponseCache(clock), new KeyedAsyncLock())
			.GetOrFetchAsync(Source, "/stale", 30);
		Assert.Equal("old", result.Body);
		Assert.Equal(200, result.StatusCode);
	}
}
