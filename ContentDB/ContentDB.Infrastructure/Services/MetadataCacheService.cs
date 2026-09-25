// ContentDB C# Mirror
// 元数据懒缓存(多源):按上游站点分别缓存与回源;命中且新鲜直接返回;
// 未命中/过期回源、按该站 baseUrl 改写 URL、写入缓存;回源失败可降级返回陈旧缓存。

using System.Collections.Concurrent;
using ContentDB.Core.Abstractions;
using ContentDB.Core.Configuration;
using ContentDB.Core.Entities;
using ContentDB.Core.Services;
using ContentDB.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ContentDB.Infrastructure.Services;

public sealed class MetadataCacheService : IMetadataCacheService
{
	private readonly MirrorDbContext _db;
	private readonly IUpstreamClient _upstream;
	private readonly MirrorOptions _options;
	private readonly ILogger<MetadataCacheService> _logger;

	// L1:进程内热缓存,吸收热点 key 的数据库查询与回源压力;超限整表清空(代价可接受)。
	private static readonly ConcurrentDictionary<string, (CachedApiResult Result, DateTimeOffset FreshUntil)> L1 = new();
	private const int L1Capacity = 500;
	private static readonly TimeSpan L1MaxFresh = TimeSpan.FromSeconds(60);

	// single-flight:同一 key 的并发回源合并为一次,防止热门内容击穿时重复跨境拉取。
	private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new();

	public MetadataCacheService(
		MirrorDbContext db,
		IUpstreamClient upstream,
		IOptions<MirrorOptions> options,
		ILogger<MetadataCacheService> logger)
	{
		_db = db;
		_upstream = upstream;
		_options = options.Value;
		_logger = logger;
	}

	private static string BuildKey(string sourceId, string path, string? lang)
		=> $"GET:[{sourceId}]{path}|lang={lang ?? ""}";

	public async Task<CachedApiResult> GetOrFetchAsync(
		UpstreamSiteOptions source,
		string relativePathAndQuery,
		int ttlSeconds,
		string? acceptLanguage = null,
		string? userAgent = null,
		bool rewriteUrls = true,
		CancellationToken ct = default)
	{
		var now = DateTimeOffset.UtcNow;
		var key = BuildKey(source.Id, relativePathAndQuery, acceptLanguage);

		// 该站 baseUrl -> 本镜像对外 URL 的改写器
		var rewriter = new UrlRewriter(source.BaseUrl, _options.PublicBaseUrl);

		// L1:命中直接返回,不打数据库。
		if (L1.TryGetValue(key, out var hot) && hot.FreshUntil > now)
			return hot.Result;

		// 读缓存:缓存库不可用时降级为"无缓存",继续回源(保底,避免整源丢失)。
		CachedResponse? cached = null;
		var cacheDbAvailable = true;
		try
		{
			cached = await _db.CachedResponses.FirstOrDefaultAsync(x => x.CacheKey == key, ct);
		}
		catch (Exception ex) when (!ct.IsCancellationRequested)
		{
			cacheDbAvailable = false;
			_logger.LogWarning(ex, "读取元数据缓存失败(缓存库不可用),降级为直接回源 [{Source}]: {Path}",
				source.Id, relativePathAndQuery);
		}

		if (cached is not null && cached.IsFresh(now))
		{
			var fresh = new CachedApiResult(cached.StatusCode, cached.Body, cached.ContentType);
			Remember(key, fresh, cached.ExpiresAt, now);
			return fresh;
		}

		// single-flight:并发同一 key 只放一个进回源,其余等待后走双重检查。
		var gate = Gates.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
		await gate.WaitAsync(ct);
		try
		{
			// 双重检查:等待期间可能已被并发的先行者填好。
			if (L1.TryGetValue(key, out hot) && hot.FreshUntil > now)
				return hot.Result;
			if (cacheDbAvailable)
			{
				cached = await _db.CachedResponses.FirstOrDefaultAsync(x => x.CacheKey == key, ct);
				if (cached is not null && cached.IsFresh(now))
				{
					var fresh = new CachedApiResult(cached.StatusCode, cached.Body, cached.ContentType);
					Remember(key, fresh, cached.ExpiresAt, now);
					return fresh;
				}
			}

			try
			{
				var resp = await _upstream.GetJsonAsync(source.BaseUrl, relativePathAndQuery, acceptLanguage, userAgent, ct);
				var body = rewriteUrls ? rewriter.Rewrite(resp.Body) : resp.Body;

				// 仅在缓存库可用时写缓存;写失败不影响返回。
				if (resp.StatusCode == 200 && cacheDbAvailable)
				{
					try
					{
						await UpsertAsync(key, body, resp.ContentType, resp.StatusCode, now, ttlSeconds, ct);
					}
					catch (Exception ex) when (!ct.IsCancellationRequested)
					{
						_logger.LogWarning(ex, "写入元数据缓存失败,已忽略 [{Source}]: {Path}",
							source.Id, relativePathAndQuery);
					}
				}

				var result = new CachedApiResult(resp.StatusCode, body, resp.ContentType ?? "application/json");
				if (cacheDbAvailable)
					Remember(key, result, now.AddSeconds(ttlSeconds), now);
				return result;
			}
			catch (Exception ex)
			{
				_logger.LogWarning(ex, "回源失败 [{Source}]: {Path}", source.Id, relativePathAndQuery);

				if (_options.ServeStaleOnUpstreamError && cached is not null)
				{
					_logger.LogInformation("降级返回陈旧缓存 [{Source}]: {Path}", source.Id, relativePathAndQuery);
					return new CachedApiResult(cached.StatusCode, cached.Body, cached.ContentType);
				}

				return new CachedApiResult(502, "{\"error\":\"upstream unavailable\"}", "application/json");
			}
		}
		finally
		{
			gate.Release();
		}
	}

	/// <summary>写入 L1;新鲜度取 L2 过期时间与上限(60s)的较小者,保证元数据及时失效。</summary>
	private static void Remember(string key, CachedApiResult result, DateTimeOffset? l2ExpiresAt, DateTimeOffset now)
	{
		if (result.StatusCode != 200)
			return;
		var freshUntil = now + L1MaxFresh;
		if (l2ExpiresAt is { } e && e < freshUntil)
			freshUntil = e;
		if (L1.Count >= L1Capacity)
			L1.Clear();
		L1[key] = (result, freshUntil);
	}

	private async Task UpsertAsync(
		string key, string body, string? contentType, int status,
		DateTimeOffset now, int ttlSeconds, CancellationToken ct)
	{
		var entity = await _db.CachedResponses.FirstOrDefaultAsync(x => x.CacheKey == key, ct);
		if (entity is null)
		{
			entity = new CachedResponse { CacheKey = key };
			_db.CachedResponses.Add(entity);
		}

		entity.Body = body;
		entity.ContentType = contentType;
		entity.StatusCode = status;
		entity.FetchedAt = now;
		entity.ExpiresAt = now.AddSeconds(ttlSeconds);

		await _db.SaveChangesAsync(ct);
	}
}
