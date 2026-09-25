// ContentDB C# Mirror
// 只读镜像 API。/api/packages 与 /api/updates 走多源合并;其余 GET /api/* 端点
// 走"回源(默认上游)+懒缓存+URL 改写"透传。
//
// 多源说明:
// - 列表/更新:合并本站 + 所有启用上游(见 MergedReadService)。
// - 单包详情/发布/截图等:优先本站(若存在),否则回退到指定 source(?source=xxx)或默认上游。

using ContentDB.Core.Abstractions;
using ContentDB.Core.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace ContentDB.Api.Controllers;

[ApiController]
public sealed class ApiMirrorController : ControllerBase
{
	private readonly IMetadataCacheService _cache;
	private readonly IMergedReadService _merged;
	private readonly ISourceResolver _sources;
	private readonly MetadataTtlOptions _ttl;

	public ApiMirrorController(
		IMetadataCacheService cache,
		IMergedReadService merged,
		ISourceResolver sources,
		IOptions<MirrorOptions> options)
	{
		_cache = cache;
		_merged = merged;
		_sources = sources;
		_ttl = options.Value.MetadataTtlSeconds;
	}

	private string PathAndQuery => Request.Path + Request.QueryString;

	private string? AcceptLanguage => Request.Headers.AcceptLanguage.ToString() is { Length: > 0 } al ? al : null;

	private string? UserAgent => Request.Headers.UserAgent.ToString() is { Length: > 0 } ua ? ua : null;

	private bool IsLuantiClient
	{
		get
		{
			var ua = UserAgent ?? "";
			return ua.StartsWith("Luanti", StringComparison.OrdinalIgnoreCase)
				|| ua.StartsWith("Minetest", StringComparison.OrdinalIgnoreCase);
		}
	}

	private Dictionary<string, List<string>> QueryArgs()
	{
		var dict = new Dictionary<string, List<string>>();
		foreach (var kv in Request.Query)
			dict[kv.Key] = kv.Value.Where(v => v is not null).Select(v => v!).ToList();
		return dict;
	}

	/// <summary>200 响应附带客户端缓存头,让浏览器/CDN 分担回源流量;TTL 收敛在 30~300s。</summary>
	private void SetClientCache(int? ttl = null)
	{
		if (Response.StatusCode == 200)
			Response.Headers.CacheControl = $"public, max-age={Math.Clamp(ttl ?? 60, 30, 300)}";
	}

	/// <summary>解析目标上游:?source=id 指定,否则默认上游。无可用上游返回 null。</summary>
	private UpstreamSiteOptions? ResolveSource()
	{
		var id = Request.Query["source"].ToString();
		if (!string.IsNullOrEmpty(id))
			return _sources.FindById(id);
		return _sources.Default;
	}

	/// <summary>透传到指定上游(默认上游)。</summary>
	private async Task<IActionResult> Proxy(int ttl)
	{
		var site = ResolveSource();
		if (site is null)
			return new ContentResult { StatusCode = 502, Content = "{\"error\":\"no upstream configured\"}", ContentType = "application/json" };

		var result = await _cache.GetOrFetchAsync(
			site, PathAndQuery, ttl, AcceptLanguage, UserAgent, rewriteUrls: true, HttpContext.RequestAborted);

		Response.Headers.Vary = "Accept-Language";
		SetClientCache(ttl);
		return new ContentResult
		{
			StatusCode = result.StatusCode,
			Content = result.Body,
			ContentType = result.ContentType ?? "application/json",
		};
	}

	// ---- 包发现 / 元数据(多源合并)----

	[HttpGet("/api/packages/")]
	public async Task<IActionResult> Packages()
	{
		var fmt = Request.Query["fmt"].ToString() is { Length: > 0 } f ? f : null;
		var result = await _merged.GetPackagesAsync(QueryArgs(), AcceptLanguage, UserAgent, fmt, IsLuantiClient, HttpContext.RequestAborted);
		Response.Headers.Vary = "Accept-Language";
		SetClientCache(60);
		return new ContentResult { StatusCode = result.StatusCode, Content = result.Body, ContentType = result.ContentType ?? "application/json" };
	}

	[HttpGet("/api/packages/{author}/{name}/")]
	public async Task<IActionResult> PackageView(string author, string name)
	{
		// 单包详情:优先本站(若存在),否则回退上游。修复本站独有包在详情页 404 的问题。
		var local = await _merged.GetLocalPackageAsync(author, name, HttpContext.RequestAborted);
		if (local is not null)
		{
			SetClientCache(60);
			return new ContentResult { StatusCode = local.StatusCode, Content = local.Body, ContentType = local.ContentType ?? "application/json" };
		}
		return await Proxy(_ttl.Default);
	}

	[HttpGet("/api/packages/{author}/{name}/for-client/")]
	public Task<IActionResult> ForClient(string author, string name) => Proxy(_ttl.ForClient);

	[HttpGet("/api/packages/{author}/{name}/for-client/reviews/")]
	public Task<IActionResult> ForClientReviews(string author, string name) => Proxy(_ttl.ForClient);

	[HttpGet("/api/packages/{author}/{name}/hypertext/")]
	public Task<IActionResult> Hypertext(string author, string name) => Proxy(_ttl.Default);

	[HttpGet("/api/packages/{author}/{name}/dependencies/")]
	public Task<IActionResult> Dependencies(string author, string name) => Proxy(_ttl.Dependencies);

	// ---- 发布 ----

	[HttpGet("/api/releases/")]
	public Task<IActionResult> Releases() => Proxy(_ttl.Default);

	[HttpGet("/api/packages/{author}/{name}/releases/")]
	public async Task<IActionResult> PackageReleases(string author, string name)
	{
		// 优先本站(若该包为本站包),否则回退上游。
		var local = await _merged.GetLocalReleasesAsync(author, name, HttpContext.RequestAborted);
		if (local is not null)
			return new ContentResult { StatusCode = local.StatusCode, Content = local.Body, ContentType = local.ContentType ?? "application/json" };
		return await Proxy(_ttl.Default);
	}

	[HttpGet("/api/packages/{author}/{name}/releases/{id:int}/")]
	public Task<IActionResult> ReleaseView(string author, string name, int id) => Proxy(_ttl.Default);

	// ---- 截图 ----

	[HttpGet("/api/packages/{author}/{name}/screenshots/")]
	public async Task<IActionResult> Screenshots(string author, string name)
	{
		// 优先本站(若该包为本站包),否则回退上游。
		var local = await _merged.GetLocalScreenshotsAsync(author, name, HttpContext.RequestAborted);
		if (local is not null)
			return new ContentResult { StatusCode = local.StatusCode, Content = local.Body, ContentType = local.ContentType ?? "application/json" };
		return await Proxy(_ttl.Default);
	}

	[HttpGet("/api/packages/{author}/{name}/screenshots/{id:int}/")]
	public Task<IActionResult> Screenshot(string author, string name, int id) => Proxy(_ttl.Default);

	// ---- 评价 ----

	[HttpGet("/api/packages/{author}/{name}/reviews/")]
	public Task<IActionResult> Reviews(string author, string name) => Proxy(_ttl.Default);

	[HttpGet("/api/reviews/")]
	public Task<IActionResult> AllReviews() => Proxy(_ttl.Default);

	// ---- 统计 / 分数 ----

	[HttpGet("/api/packages/{author}/{name}/stats/")]
	public Task<IActionResult> PackageStats(string author, string name) => Proxy(_ttl.Default);

	[HttpGet("/api/package_stats/")]
	public Task<IActionResult> AllPackageStats() => Proxy(900);

	[HttpGet("/api/scores/")]
	public Task<IActionResult> Scores() => Proxy(900);

	[HttpGet("/api/users/{username}/stats/")]
	public Task<IActionResult> UserStats(string username) => Proxy(_ttl.Default);

	// ---- 参考数据 ----

	[HttpGet("/api/tags/")]
	public Task<IActionResult> Tags() => Proxy(_ttl.Tags);

	[HttpGet("/api/content_warnings/")]
	public Task<IActionResult> ContentWarnings() => Proxy(_ttl.ContentWarnings);

	[HttpGet("/api/licenses/")]
	public Task<IActionResult> Licenses() => Proxy(_ttl.Licenses);

	[HttpGet("/api/minetest_versions/")]
	public Task<IActionResult> MinetestVersions() => Proxy(_ttl.Default);

	[HttpGet("/api/languages/")]
	public Task<IActionResult> Languages() => Proxy(_ttl.Default);

	[HttpGet("/api/cdb_schema/")]
	public Task<IActionResult> CdbSchema() => Proxy(_ttl.Tags);

	// ---- 聚合 / 客户端支持 ----

	[HttpGet("/api/homepage/")]
	public Task<IActionResult> Homepage() => Proxy(_ttl.Homepage);

	[HttpGet("/api/updates/")]
	public async Task<IActionResult> Updates()
	{
		var result = await _merged.GetUpdatesAsync(QueryArgs(), UserAgent, HttpContext.RequestAborted);
		SetClientCache(60);
		return new ContentResult { StatusCode = result.StatusCode, Content = result.Body, ContentType = result.ContentType ?? "application/json" };
	}

	[HttpGet("/api/uploads/")]
	public Task<IActionResult> Uploads() => Proxy(_ttl.Default);

	[HttpGet("/api/dependencies/")]
	public Task<IActionResult> AllDependencies() => Proxy(_ttl.Default);

	[HttpGet("/api/topics/")]
	public Task<IActionResult> Topics() => Proxy(_ttl.Default);

	[HttpGet("/api/collections/")]
	public Task<IActionResult> Collections() => Proxy(_ttl.Default);

	[HttpGet("/api/collections/{author}/{name}/")]
	public Task<IActionResult> CollectionView(string author, string name) => Proxy(_ttl.Default);

	[HttpGet("/api/users/{username}/")]
	public Task<IActionResult> UserView(string username) => Proxy(_ttl.Default);
}
