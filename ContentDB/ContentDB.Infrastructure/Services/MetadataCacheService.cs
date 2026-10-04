// ContentDB C# Mirror: scoped persistence and upstream access, host-owned hot cache and keyed exclusion.
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ContentDB.Core.Abstractions;
using ContentDB.Core.Configuration;
using ContentDB.Core.Entities;
using ContentDB.Core.Services;
using ContentDB.Infrastructure.Caching;
using ContentDB.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ContentDB.Infrastructure.Services;

public sealed class MetadataCacheService(
	MirrorDbContext db,
	IUpstreamClient upstream,
	IOptions<MirrorOptions> options,
	ILogger<MetadataCacheService> logger,
	MetadataResponseCache hotCache,
	KeyedAsyncLock gates,
	TimeProvider clock) : IMetadataCacheService
{
	internal static string BuildKey(UpstreamSiteOptions source, string path, string? lang,
		string? userAgent, bool rewriteUrls, string publicBaseUrl)
	{
		// Versioned, bounded and unambiguous. Every response-affecting input is isolated,
		// including source URL changes and raw versus rewritten representations.
		var identity = JsonSerializer.Serialize(new[] { source.Id, source.BaseUrl, path,
			lang ?? "", userAgent ?? "", rewriteUrls ? publicBaseUrl : null });
		return "metadata:v2:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
	}

	public async Task<CachedApiResult> GetOrFetchAsync(
		UpstreamSiteOptions source, string relativePathAndQuery, int ttlSeconds,
		string? acceptLanguage = null, string? userAgent = null, bool rewriteUrls = true,
		CancellationToken ct = default)
	{
		ct.ThrowIfCancellationRequested();
		var key = BuildKey(source, relativePathAndQuery, acceptLanguage, userAgent,
			rewriteUrls, options.Value.PublicBaseUrl);
		if (hotCache.TryGet(key, out var hot)) return hot;

		// Serialize misses before database reads as well as upstream fetches.
		using var lease = await gates.AcquireAsync(key, ct);
		if (hotCache.TryGet(key, out hot)) return hot;

		CachedResponse? cached = null;
		var cacheDbAvailable = true;
		try
		{
			// A scoped context may have tracked an earlier version while waiting.
			cached = await db.CachedResponses.AsNoTracking().FirstOrDefaultAsync(x => x.CacheKey == key, ct);
		}
		catch (Exception ex) when (!ct.IsCancellationRequested)
		{
			cacheDbAvailable = false;
			logger.LogWarning(ex, "Metadata cache read failed [{Source}]: {Path}", source.Id, relativePathAndQuery);
		}

		if (cached is not null && cached.IsFresh(clock.GetUtcNow()))
		{
			var fresh = new CachedApiResult(cached.StatusCode, cached.Body, cached.ContentType);
			hotCache.Remember(key, fresh, cached.ExpiresAt);
			return fresh;
		}

		UpstreamJsonResponse response;
		try
		{
			response = await upstream.GetJsonAsync(source.BaseUrl, relativePathAndQuery, acceptLanguage, userAgent, ct);
		}
		catch (Exception ex) when (!ct.IsCancellationRequested)
		{
			logger.LogWarning(ex, "Metadata upstream failed [{Source}]: {Path}", source.Id, relativePathAndQuery);
			return StaleOrUnavailable(cached);
		}
		ct.ThrowIfCancellationRequested();
		if (response.StatusCode >= 500 && options.Value.ServeStaleOnUpstreamError && cached is not null)
			return new CachedApiResult(cached.StatusCode, cached.Body, cached.ContentType);

		var body = rewriteUrls
			? new UrlRewriter(source.BaseUrl, options.Value.PublicBaseUrl).Rewrite(response.Body)
			: response.Body;
		var result = new CachedApiResult(response.StatusCode, body, response.ContentType ?? "application/json");
		// TTL begins after the fetch, not before waiting for a lock or a slow upstream.
		var fetchedAt = clock.GetUtcNow();
		var expiresAt = fetchedAt.AddSeconds(ttlSeconds);
		if (response.StatusCode == 200 && ttlSeconds > 0)
		{
			if (cacheDbAvailable)
			{
				try
				{
					await UpsertAsync(key, result, fetchedAt, expiresAt, ct);
				}
				catch (Exception ex) when (!ct.IsCancellationRequested)
				{
					// Failed tracked writes must not leak into later SaveChanges calls.
					foreach (var entry in db.ChangeTracker.Entries<CachedResponse>().Where(x => x.Entity.CacheKey == key).ToList())
						entry.State = EntityState.Detached;
					logger.LogWarning(ex, "Metadata cache write failed [{Source}]: {Path}", source.Id, relativePathAndQuery);
				}
			}
			// L1 remains useful even when the persistent cache is unavailable.
			hotCache.Remember(key, result, expiresAt);
		}
		return result;
	}

	private CachedApiResult StaleOrUnavailable(CachedResponse? cached)
		=> options.Value.ServeStaleOnUpstreamError && cached is not null
			? new CachedApiResult(cached.StatusCode, cached.Body, cached.ContentType)
			: new CachedApiResult(502, "{\"error\":\"upstream unavailable\"}", "application/json");

	private async Task UpsertAsync(string key, CachedApiResult result,
		DateTimeOffset fetchedAt, DateTimeOffset expiresAt, CancellationToken ct)
	{
		var entity = await db.CachedResponses.FirstOrDefaultAsync(x => x.CacheKey == key, ct);
		if (entity is null)
		{
			entity = new CachedResponse { CacheKey = key };
			db.CachedResponses.Add(entity);
		}
		entity.Body = result.Body;
		entity.ContentType = result.ContentType;
		entity.StatusCode = result.StatusCode;
		entity.FetchedAt = fetchedAt;
		entity.ExpiresAt = expiresAt;
		await db.SaveChangesAsync(ct);
	}
}
