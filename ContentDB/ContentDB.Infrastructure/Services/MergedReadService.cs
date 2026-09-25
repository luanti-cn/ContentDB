// ContentDB C# —— 合并读取服务实现(多源)
// 本站(AppDbContext)+ 多个上游站点(MetadataCacheService 代理/缓存),读取时合并、去重、打 origin/source。
// 排序:本站按时间倒序在最前;各外站按站点 Order,站内按时间倒序,依次拼接在后。
// Luanti 客户端:在简介前加站点标签(如 [Luanti.org]/[Luanti.cn])。

using System.Text.Json.Nodes;
using ContentDB.Core.Abstractions;
using ContentDB.Core.Configuration;
using ContentDB.Core.Domain;
using ContentDB.Core.Query;
using ContentDB.Core.Serialization;
using ContentDB.Infrastructure.Data;
using ContentDB.Infrastructure.Query;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ContentDB.Infrastructure.Services;

public sealed class MergedReadService : IMergedReadService
{
	private readonly AppDbContext _db;
	private readonly IMetadataCacheService _cache;
	private readonly ISourceResolver _sources;
	private readonly MirrorOptions _options;
	private readonly PackageSerializer _serializer;
	private readonly ILogger<MergedReadService> _logger;

	public MergedReadService(
		AppDbContext db,
		IMetadataCacheService cache,
		ISourceResolver sources,
		IOptions<MirrorOptions> options,
		ILogger<MergedReadService> logger)
	{
		_db = db;
		_cache = cache;
		_sources = sources;
		_options = options.Value;
		_serializer = new PackageSerializer(_options.PublicBaseUrl);
		_logger = logger;
	}

	public async Task<MergedResult> GetPackagesAsync(
		IReadOnlyDictionary<string, List<string>> args,
		string? acceptLanguage,
		string? userAgent,
		string? fmt,
		bool isLuantiClient,
		CancellationToken ct = default)
	{
		var merged = new JsonArray();
		var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		// 1) 本站结果(按创建时间倒序,已在 QueryLocal 中排序)——恒在最前。
		//    本站库不可用时降级为空,不阻断上游合并(保底)。
		List<(Package pkg, int? releaseId)> local;
		try
		{
			local = await QueryLocalAsync(args, ct);
		}
		catch (Exception ex) when (!ct.IsCancellationRequested)
		{
			_logger.LogWarning(ex, "查询本站包失败(本站库不可用),仅返回上游结果");
			local = new List<(Package, int?)>();
		}
		var localLabel = isLuantiClient ? NullIfEmpty(_sources.Local.Label) : null;

		foreach (var (pkg, releaseId) in local)
		{
			seenKeys.Add($"{pkg.Author.Username}/{pkg.Name}");
			if (fmt == "keys")
			{
				merged.Add(_serializer.ToKeyDict(pkg));
			}
			else
			{
				merged.Add(_serializer.ToShortDict(
					pkg, releaseId, includeVcs: fmt == "vcs",
					sourceId: _sources.Local.Id, sourceName: _sources.Local.Name,
					luantiLabel: localLabel));
			}
		}

		// 2) 各上游站点(按 Order),站内保持其自身返回顺序(通常已是时间倒序);
		//    去重:同 author/name 已被本站或更靠前的站点收录则跳过。
		foreach (var site in _sources.Upstreams)
		{
			var items = await FetchUpstreamPackagesAsync(site, args, acceptLanguage, userAgent, seenKeys, fmt, isLuantiClient, ct);
			foreach (var n in items)
				merged.Add(n);
		}

		return new MergedResult(200, merged.ToJsonString(), "application/json");
	}

	private async Task<List<(Package pkg, int? releaseId)>> QueryLocalAsync(
		IReadOnlyDictionary<string, List<string>> args, CancellationToken ct)
	{
		var pq = PackageQuery.FromDictionary(args);

		var protocol = ParseInt(Single(args, "protocol_version"));
		var engine = Single(args, "engine_version");
		if (protocol is not null || !string.IsNullOrEmpty(engine))
			pq.Version = await ResolveVersionAsync(engine, protocol, ct);

		int? gameId = null;
		if (!string.IsNullOrEmpty(pq.GameKey))
			gameId = await ResolvePackageIdByKeyAsync(pq.GameKey, ct);

		int? authorId = null;
		if (!string.IsNullOrEmpty(pq.Author))
			authorId = await _db.Users.Where(u => u.Username == pq.Author)
				.Select(u => (int?)u.Id).FirstOrDefaultAsync(ct);

		var licenseIds = pq.LicenseNames.Count == 0
			? Array.Empty<int>()
			: await _db.Licenses.Where(l => pq.LicenseNames.Select(n => n.ToLower()).Contains(l.Name.ToLower()))
				.Select(l => l.Id).ToArrayAsync(ct);

		IQueryable<Package> query = _db.Packages
			.Include(p => p.Author)
			.Include(p => p.Screenshots)
			.Include(p => p.Aliases);

		query = new PackageQueryBuilder(pq).Apply(query, gameId, authorId, licenseIds);

		var packages = await query.ToListAsync(ct);

		// 本站排序:按创建时间倒序(需求:本站按时间排最前)。
		// 若查询显式指定了 sort,则尊重 QueryBuilder 的排序;否则强制时间倒序。
		if (string.IsNullOrEmpty(pq.OrderBy))
			packages = packages.OrderByDescending(p => p.CreatedAt).ToList();

		var result = new List<(Package, int?)>();
		foreach (var p in packages)
			result.Add((p, await ResolveReleaseIdAsync(p.Id, pq.Version?.Id, ct)));

		if (pq.Version is not null)
			result = result.Where(x => x.Item2 is not null).ToList();

		return result;
	}

	private async Task<List<JsonNode>> FetchUpstreamPackagesAsync(
		UpstreamSiteOptions site,
		IReadOnlyDictionary<string, List<string>> args,
		string? acceptLanguage, string? userAgent,
		HashSet<string> seenKeys, string? fmt, bool isLuantiClient, CancellationToken ct)
	{
		var result = new List<JsonNode>();
		try
		{
			var path = "/api/packages/" + BuildQueryString(args);
			var upstream = await _cache.GetOrFetchAsync(site, path, _options.MetadataTtlSeconds.Packages,
				acceptLanguage, userAgent, rewriteUrls: true, ct);

			if (upstream.StatusCode != 200)
				return result;

			var node = JsonNode.Parse(upstream.Body);
			if (node is not JsonArray arr) return result;

			var label = isLuantiClient ? NullIfEmpty(site.Label) : null;

			foreach (var item in arr)
			{
				if (item is not JsonObject o) continue;
				var author = o["author"]?.GetValue<string>();
				var name = o["name"]?.GetValue<string>();
				var key = author is not null && name is not null ? $"{author}/{name}" : null;

				// 去重:已被本站或更靠前站点收录则跳过
				if (key is not null && !seenKeys.Add(key))
					continue;

				o["origin"] = ContentOrigin.UPSTREAM.ToString().ToLowerInvariant();
				o["source"] = site.Id;
				o["source_name"] = site.Name;

				// Luanti 客户端:简介前加站点标签
				if (label is not null && fmt is not "keys")
				{
					var sd = o["short_description"]?.GetValue<string?>();
					o["short_description"] = string.IsNullOrEmpty(sd) ? label : $"{label} {sd}";
				}

				result.Add(o.DeepClone());
			}
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "合并上游包列表失败 [{Source}],跳过该源", site.Id);
		}
		return result;
	}

	public async Task<MergedResult> GetUpdatesAsync(
		IReadOnlyDictionary<string, List<string>> args,
		string? userAgent,
		CancellationToken ct = default)
	{
		var protocol = ParseInt(Single(args, "protocol_version"));
		var engine = Single(args, "engine_version");
		LuantiRelease? version = null;
		if (protocol is not null || !string.IsNullOrEmpty(engine))
			version = await ResolveVersionAsync(engine, protocol, ct);

		var map = new JsonObject();

		// 各上游先填充(顺序:Order 升序;后填充的同键会覆盖,但 author/name 冲突概率低)
		foreach (var site in _sources.Upstreams)
		{
			try
			{
				var path = "/api/updates/" + BuildQueryString(args);
				var upstream = await _cache.GetOrFetchAsync(site, path, _options.MetadataTtlSeconds.Updates,
					null, userAgent, rewriteUrls: false, ct);
				if (upstream.StatusCode == 200 && JsonNode.Parse(upstream.Body) is JsonObject uo)
					foreach (var kv in uo)
						if (!map.ContainsKey(kv.Key))
							map[kv.Key] = kv.Value?.DeepClone();
			}
			catch (Exception ex)
			{
				_logger.LogWarning(ex, "合并上游 updates 失败 [{Source}]", site.Id);
			}
		}

		// 本站覆盖(本站优先)
		var localPackages = await _db.Packages
			.Where(p => p.State == PackageState.APPROVED)
			.Include(p => p.Author)
			.Select(p => new { p.Id, Author = p.Author.Username, p.Name })
			.ToListAsync(ct);

		foreach (var p in localPackages)
		{
			var relId = await ResolveReleaseIdAsync(p.Id, version?.Id, ct);
			if (relId is not null)
				map[$"{p.Author}/{p.Name}"] = relId;
		}

		return new MergedResult(200, map.ToJsonString(), "application/json");
	}

	// ---- 本站单包(详情/发布/截图):命中返回本站数据,未命中返回 null 由控制器回退上游 ----

	public async Task<MergedResult?> GetLocalPackageAsync(string author, string name, CancellationToken ct = default)
	{
		var pkg = await _db.Packages
			.Include(p => p.Author)
			.Include(p => p.Screenshots)
			.Include(p => p.Aliases)
			.Include(p => p.Tags)
			.Include(p => p.ContentWarnings)
			.Include(p => p.Provides)
			.Include(p => p.License)
			.Include(p => p.MediaLicense)
			.FirstOrDefaultAsync(p => p.Author.Username == author && p.Name == name, ct);

		if (pkg is null) return null;

		var releaseId = await ResolveReleaseIdAsync(pkg.Id, null, ct);
		var obj = _serializer.ToDict(pkg, releaseId);
		return new MergedResult(200, obj.ToJsonString(), "application/json");
	}

	public async Task<MergedResult?> GetLocalReleasesAsync(string author, string name, CancellationToken ct = default)
	{
		var pkgId = await _db.Packages
			.Where(p => p.Author.Username == author && p.Name == name)
			.Select(p => (int?)p.Id)
			.FirstOrDefaultAsync(ct);

		if (pkgId is null) return null;

		var releases = await _db.Releases
			.Where(r => r.PackageId == pkgId && r.State == ReleaseState.APPROVED)
			.Include(r => r.MinRel)
			.Include(r => r.MaxRel)
			.OrderByDescending(r => r.Id)
			.ToListAsync(ct);

		var arr = new JsonArray();
		foreach (var r in releases)
			arr.Add(_serializer.ReleaseToDict(r));

		return new MergedResult(200, arr.ToJsonString(), "application/json");
	}

	public async Task<MergedResult?> GetLocalScreenshotsAsync(string author, string name, CancellationToken ct = default)
	{
		var pkg = await _db.Packages
			.Where(p => p.Author.Username == author && p.Name == name)
			.Select(p => new { p.Id, p.CoverImageId })
			.FirstOrDefaultAsync(ct);

		if (pkg is null) return null;

		var screenshots = await _db.Screenshots
			.Where(s => s.PackageId == pkg.Id && s.Approved)
			.OrderBy(s => s.Order)
			.ToListAsync(ct);

		var arr = new JsonArray();
		foreach (var s in screenshots)
		{
			var obj = _serializer.ScreenshotToDict(s);
			obj["is_cover_image"] = pkg.CoverImageId == s.Id;
			arr.Add(obj);
		}

		return new MergedResult(200, arr.ToJsonString(), "application/json");
	}

	// ---- helpers ----

	private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

	private async Task<int?> ResolveReleaseIdAsync(int packageId, int? versionId, CancellationToken ct)
	{
		var q = _db.Releases.Where(r => r.PackageId == packageId && r.State == ReleaseState.APPROVED);
		if (versionId is int vid)
			q = q.Where(r => (r.MinRelId == null || r.MinRelId <= vid) && (r.MaxRelId == null || r.MaxRelId >= vid));
		return await q.OrderByDescending(r => r.Id).Select(r => (int?)r.Id).FirstOrDefaultAsync(ct);
	}

	private async Task<int?> ResolvePackageIdByKeyAsync(string key, CancellationToken ct)
	{
		var parts = key.Split('/');
		if (parts.Length != 2) return null;
		var name = parts[1];
		var stripped = name.EndsWith("_game") ? name[..^5] : name;
		return await _db.Packages
			.Where(p => p.Author.Username == parts[0] && (p.Name == name || p.Name == stripped || p.Name == stripped + "_game"))
			.Select(p => (int?)p.Id).FirstOrDefaultAsync(ct);
	}

	private async Task<LuantiRelease?> ResolveVersionAsync(string? engine, int? protocol, CancellationToken ct)
	{
		if (!string.IsNullOrEmpty(engine))
		{
			var parts = engine.Trim().Split('.');
			if (parts.Length >= 2)
			{
				var prefix = $"{parts[0]}.{parts[1]}";
				var q = _db.LuantiReleases.Where(r => r.Name.Replace("-dev", "") == prefix);
				if (protocol is int pn) q = q.Where(r => r.Protocol == pn);
				var rel = await q.FirstOrDefaultAsync(ct);
				if (rel is not null) return rel;
			}
		}
		if (protocol is int p)
			return await _db.LuantiReleases.Where(r => r.Protocol <= p)
				.OrderByDescending(r => r.Protocol).ThenByDescending(r => r.Id).FirstOrDefaultAsync(ct);
		return null;
	}

	private static string? Single(IReadOnlyDictionary<string, List<string>> args, string key)
		=> args.TryGetValue(key, out var v) && v.Count > 0 ? v[0] : null;

	private static int? ParseInt(string? s) => int.TryParse(s, out var i) ? i : null;

	private static string BuildQueryString(IReadOnlyDictionary<string, List<string>> args)
	{
		if (args.Count == 0) return "";
		var parts = new List<string>();
		foreach (var (k, vs) in args)
			foreach (var v in vs)
				parts.Add($"{Uri.EscapeDataString(k)}={Uri.EscapeDataString(v)}");
		return "?" + string.Join("&", parts);
	}
}
