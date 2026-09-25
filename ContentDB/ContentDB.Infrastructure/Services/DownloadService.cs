// ContentDB C# —— 下载解析服务实现
// 本地发布:对象存储(S3)交付 + 下载计数/每日统计;上游:回退 FileMirrorService。

using ContentDB.Core.Abstractions;
using ContentDB.Core.Configuration;
using ContentDB.Core.Domain;
using ContentDB.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ContentDB.Infrastructure.Services;

public sealed class DownloadService : IDownloadService
{
	private readonly AppDbContext _db;
	private readonly IObjectStorage _storage;
	private readonly IFileMirrorService _fileMirror;
	private readonly IMetadataCacheService _cache;
	private readonly ISourceResolver _sources;
	private readonly StorageOptions _storageOptions;
	private readonly MirrorOptions _mirrorOptions;
	private readonly ILogger<DownloadService> _logger;

	public DownloadService(
		AppDbContext db,
		IObjectStorage storage,
		IFileMirrorService fileMirror,
		IMetadataCacheService cache,
		ISourceResolver sources,
		IOptions<StorageOptions> storageOptions,
		IOptions<MirrorOptions> mirrorOptions,
		ILogger<DownloadService> logger)
	{
		_db = db;
		_storage = storage;
		_fileMirror = fileMirror;
		_cache = cache;
		_sources = sources;
		_storageOptions = storageOptions.Value;
		_mirrorOptions = mirrorOptions.Value;
		_logger = logger;
	}

	/// <summary>按 sourceId 解析上游站点;无则用默认上游。</summary>
	private UpstreamSiteOptions? ResolveUpstream(string? sourceId)
		=> !string.IsNullOrEmpty(sourceId) ? _sources.FindById(sourceId) : _sources.Default;

	public async Task<DownloadResolution> ResolveReleaseDownloadAsync(
		string author, string name, int releaseId,
		string clientIp, string? userAgent, string? reason,
		string? sourceId = null,
		CancellationToken ct = default)
	{
		var release = await _db.Releases
			.Include(r => r.Package).ThenInclude(p => p.Author)
			.FirstOrDefaultAsync(r => r.Id == releaseId
				&& r.Package.Name == name && r.Package.Author.Username == author, ct);

		if (release is not null)
		{
			await CountDownloadAsync(release, userAgent, reason, ct);
			var local = await DeliverLocalAsync(release, ct);
			return local with { SuggestedFileName = BuildFileName(name, release.Name) };
		}

		// 本地无此发布:回退到指定/默认上游代理缓存。
		var site = ResolveUpstream(sourceId);
		if (site is null)
			return new DownloadResolution(false, null, null, null, null, 404);

		var upstreamPath = $"/packages/{author}/{name}/releases/{releaseId}/download/";
		var mirror = await _fileMirror.EnsureAndResolveAsync(site, upstreamPath, userAgent, ct);
		if (!mirror.Success)
			return new DownloadResolution(false, null, null, null, null, mirror.UpstreamStatus == 0 ? 404 : mirror.UpstreamStatus);

		var upstreamVersion = await ResolveUpstreamReleaseNameAsync(site, author, name, releaseId, ct);
		return new DownloadResolution(true, mirror.RedirectUrl, mirror.Content, mirror.ContentType,
			mirror.ContentLength, 200, BuildFileName(name, upstreamVersion));
	}

	/// <summary>回源查上游 release 元数据,取版本名(name)用于拼下载文件名。失败返回 null。</summary>
	private async Task<string?> ResolveUpstreamReleaseNameAsync(UpstreamSiteOptions site, string author, string name, int releaseId, CancellationToken ct)
	{
		try
		{
			var apiPath = $"/api/packages/{author}/{name}/releases/{releaseId}/";
			var meta = await _cache.GetOrFetchAsync(site, apiPath, _mirrorOptions.MetadataTtlSeconds.Default,
				null, null, rewriteUrls: false, ct);
			if (meta.StatusCode != 200)
				return null;

			using var doc = System.Text.Json.JsonDocument.Parse(meta.Body);
			if (doc.RootElement.TryGetProperty("name", out var n) && n.ValueKind == System.Text.Json.JsonValueKind.String)
				return n.GetString();
			return null;
		}
		catch
		{
			return null;
		}
	}

	/// <summary>构造下载文件名: {包名}_{版本}.zip;版本缺失时退化为 {包名}.zip。</summary>
	private static string BuildFileName(string packageName, string? version)
	{
		string Sanitize(string s)
		{
			var chars = s.Select(c => char.IsLetterOrDigit(c) || c is '.' or '_' or '-' ? c : '_').ToArray();
			return new string(chars);
		}

		var pkg = Sanitize(packageName);
		if (string.IsNullOrWhiteSpace(version))
			return $"{pkg}.zip";
		return $"{pkg}_{Sanitize(version)}.zip";
	}

	public async Task<ObjectKeyResolution> ResolveObjectKeyAsync(
		string author, string name, int releaseId,
		string? userAgent, string? sourceId = null, CancellationToken ct = default)
	{
		var release = await _db.Releases
			.Include(r => r.Package).ThenInclude(p => p.Author)
			.FirstOrDefaultAsync(r => r.Id == releaseId
				&& r.Package.Name == name && r.Package.Author.Username == author, ct);

		if (release is not null)
		{
			var key = release.GetObjectKey();
			if (key is null)
				// 外链发布:无对象存储 key,直接给外链。
				return new ObjectKeyResolution(true, null, release.Url, 200);

			if (!await _storage.ExistsAsync(key, ct))
				return new ObjectKeyResolution(false, null, null, 404);

			return new ObjectKeyResolution(true, key, null, 200);
		}

		// 本地无此发布:回退到指定/默认上游,确保镜像入桶后返回 key。
		var site = ResolveUpstream(sourceId);
		if (site is null)
			return new ObjectKeyResolution(false, null, null, 404);

		var upstreamPath = $"/packages/{author}/{name}/releases/{releaseId}/download/";
		var mirroredKey = await _fileMirror.EnsureStoredAndGetKeyAsync(site, upstreamPath, userAgent, ct);
		if (string.IsNullOrEmpty(mirroredKey))
			return new ObjectKeyResolution(false, null, null, 502);

		return new ObjectKeyResolution(true, mirroredKey, null, 200);
	}

	public async Task CountDownloadAsync(
		string author, string name, int releaseId,
		string clientIp, string? userAgent, string? reason,
		CancellationToken ct = default)
	{
		var release = await _db.Releases
			.Include(r => r.Package)
			.FirstOrDefaultAsync(r => r.Id == releaseId
				&& r.Package.Name == name && r.Package.Author.Username == author, ct);

		// 仅本地发布计入本地统计;上游发布交由上游统计,这里不处理。
		if (release is not null)
			await CountDownloadAsync(release, userAgent, reason, ct);
	}

	public async Task<string?> GetSuggestedFileNameAsync(
		string author, string name, int releaseId, string? sourceId = null, CancellationToken ct = default)
	{
		var releaseName = await _db.Releases
			.Where(r => r.Id == releaseId && r.Package.Name == name && r.Package.Author.Username == author)
			.Select(r => (string?)r.Name)
			.FirstOrDefaultAsync(ct);

		// 本地无此发布则回源查上游版本名。
		if (releaseName is null)
		{
			var site = ResolveUpstream(sourceId);
			if (site is not null)
				releaseName = await ResolveUpstreamReleaseNameAsync(site, author, name, releaseId, ct);
		}

		return BuildFileName(name, releaseName);
	}

	private async Task<DownloadResolution> DeliverLocalAsync(PackageRelease release, CancellationToken ct)
	{
		var key = release.GetObjectKey();
		if (key is null)
		{
			// url 是外链,直接重定向
			return new DownloadResolution(true, release.Url, null, null, null, 200);
		}

		// ProxyDownloads=true(默认):镜像自身代理字节流,同源交付,规避 Luanti
		// 对跨主机 302 到对象存储/预签名 URL 的兼容问题。
		if (!_storageOptions.ProxyDownloads)
		{
			var downloadUrl = await _storage.GetDownloadUrlAsync(key, ct);
			if (!string.IsNullOrEmpty(downloadUrl))
				return new DownloadResolution(true, downloadUrl, null, null, null, 200);
		}

		var info = await _storage.GetInfoAsync(key, ct);
		var stream = await _storage.OpenReadAsync(key, ct);
		if (stream is null)
			return new DownloadResolution(false, null, null, null, null, 404);

		return new DownloadResolution(true, null, stream, info?.ContentType ?? "application/zip", info?.Size, 200);
	}

	private async Task CountDownloadAsync(PackageRelease release, string? userAgent, string? reason, CancellationToken ct)
	{
		var ua = userAgent ?? "";
		bool isLuanti = ua.StartsWith("Luanti") || ua.StartsWith("Minetest");
		bool isV510 = isLuanti && ua.Contains("5.10");

		var today = DateOnly.FromDateTime(DateTime.UtcNow);
		var stats = await _db.DailyStats.FirstOrDefaultAsync(s => s.PackageId == release.PackageId && s.Date == today, ct);
		if (stats is null)
		{
			stats = new PackageDailyStats { PackageId = release.PackageId, Date = today };
			_db.DailyStats.Add(stats);
		}

		if (isLuanti) stats.PlatformMinetest++; else stats.PlatformOther++;
		if (isV510) stats.DownloadsV510++;
		switch (reason)
		{
			case "new": stats.ReasonNew++; break;
			case "dependency": stats.ReasonDependency++; break;
			case "update": stats.ReasonUpdate++; break;
		}

		double bonus = reason switch { "new" => 1.0, "dependency" or "update" => 0.5, _ => 0.0 };

		release.Downloads++;
		var pkg = release.Package;
		pkg.Downloads++;
		pkg.ScoreDownloads += bonus;
		pkg.Score += bonus;

		await _db.SaveChangesAsync(ct);
	}
}
