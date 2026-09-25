// ContentDB C# Mirror
// 下载端点 —— 镜像的核心接缝。
// 兼容官方契约 GET /packages/<author>/<name>/releases/<id>/download/
// 以及直接文件路径 /uploads/<file>。首次未命中同步拉取入 S3,然后 302 到 CDN(或代理流)。

using System.Text.Json;
using ContentDB.Core.Abstractions;
using ContentDB.Core.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace ContentDB.Api.Controllers;

[ApiController]
public sealed class DownloadController : ControllerBase
{
	private readonly IFileMirrorService _files;
	private readonly IMetadataCacheService _cache;
	private readonly IDownloadService _downloads;
	private readonly IObjectStorage _storage;
	private readonly ISourceResolver _sources;
	private readonly IThumbnailService _thumbnails;
	private readonly MirrorOptions _options;
	private readonly ILogger<DownloadController> _logger;

	public DownloadController(
		IFileMirrorService files,
		IMetadataCacheService cache,
		IDownloadService downloads,
		IObjectStorage storage,
		ISourceResolver sources,
		IThumbnailService thumbnails,
		IOptions<MirrorOptions> options,
		ILogger<DownloadController> logger)
	{
		_files = files;
		_cache = cache;
		_downloads = downloads;
		_storage = storage;
		_sources = sources;
		_thumbnails = thumbnails;
		_options = options.Value;
		_logger = logger;
	}

	private string? UserAgent => Request.Headers.UserAgent.ToString() is { Length: > 0 } ua ? ua : null;

	private string ClientIp => Request.Headers["X-Forwarded-For"].ToString() is { Length: > 0 } xff
		? xff.Split(',')[0].Trim()
		: HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";

	private string? SourceId => Request.Query["source"].ToString() is { Length: > 0 } s ? s : null;

	/// <summary>?source=id 指定上游;否则默认上游。用于 uploads/thumbnails 回源。</summary>
	private UpstreamSiteOptions? ResolveUpstream()
		=> SourceId is not null ? _sources.FindById(SourceId) : _sources.Default;

	/// <summary>
	/// 官方风格下载端点。
	/// - Web 浏览器:302 直接跳到对象存储(预签名)链接,省镜像带宽;文件名通过预签名的
	///   response-content-disposition 指定,浏览器保存为“包名_版本.zip”。
	/// - Luanti 客户端(UA 以 Luanti/Minetest 开头):代理字节流(不 302,规避客户端跨主机重定向问题)。
	/// </summary>
	[HttpGet("/packages/{author}/{name}/releases/{id:int}/download/")]
	public async Task<IActionResult> DownloadRelease(string author, string name, int id)
	{
		var reason = Request.Query["reason"].ToString() is { Length: > 0 } r ? r : null;

		// Web 浏览器 → 直接 302 到对象存储链接(带文件名)。
		if (!IsLuantiClient())
		{
			var keyRes = await _downloads.ResolveObjectKeyAsync(author, name, id, UserAgent, SourceId, HttpContext.RequestAborted);
			if (keyRes.Found)
			{
				if (!string.IsNullOrEmpty(keyRes.ExternalUrl))
					return Redirect(keyRes.ExternalUrl);

				var fileName = await _downloads.GetSuggestedFileNameAsync(author, name, id, SourceId, HttpContext.RequestAborted);
				var directUrl = await _storage.GetPresignedUrlAsync(
					keyRes.ObjectKey!, null, fileName, HttpContext.RequestAborted);
				if (!string.IsNullOrEmpty(directUrl))
				{
					await _downloads.CountDownloadAsync(author, name, id, ClientIp, UserAgent, reason, HttpContext.RequestAborted);
					return Redirect(directUrl);
				}
			}
			// 直链不可用则回退到代理流(下面统一处理)。
		}

		// Luanti 客户端(或直链回退)→ 代理字节流。
		var res = await _downloads.ResolveReleaseDownloadAsync(
			author, name, id, ClientIp, UserAgent, reason, SourceId, HttpContext.RequestAborted);

		if (!res.Found)
			return StatusCode(res.StatusCode == 0 ? 404 : res.StatusCode);

		if (!string.IsNullOrEmpty(res.RedirectUrl))
			return Redirect(res.RedirectUrl);

		if (res.Content is not null)
		{
			var fileName = string.IsNullOrEmpty(res.SuggestedFileName) ? null : res.SuggestedFileName;
			// release 文件内容不可变:允许客户端/CDN 长缓存
			Response.Headers.CacheControl = "public, max-age=31536000, immutable";
			return File(res.Content, res.ContentType ?? "application/zip", fileName);
		}

		return StatusCode(502);
	}

	private bool IsLuantiClient()
	{
		var ua = UserAgent ?? "";
		return ua.StartsWith("Luanti", StringComparison.OrdinalIgnoreCase)
			|| ua.StartsWith("Minetest", StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>
	/// 专用端点:获取某个 release 在对象存储中的临时(预签名)链接。
	/// 默认 302 直接跳转到临时链接;带 ?format=json 时改为返回 JSON( { url, expires_in } )。
	/// 可用 ?expires=秒数 覆盖有效期。此端点不计入下载统计。
	/// </summary>
	[HttpGet("/packages/{author}/{name}/releases/{id:int}/temp-link/")]
	public async Task<IActionResult> TempLink(string author, string name, int id)
	{
		var resolution = await _downloads.ResolveObjectKeyAsync(author, name, id, UserAgent, SourceId, HttpContext.RequestAborted);
		if (!resolution.Found)
			return StatusCode(resolution.StatusCode == 0 ? 404 : resolution.StatusCode);

		// 外链发布:无对象存储对象,直接给外链。
		if (!string.IsNullOrEmpty(resolution.ExternalUrl))
			return DeliverLink(resolution.ExternalUrl, null);

		int? expires = null;
		if (int.TryParse(Request.Query["expires"], out var e) && e > 0)
			expires = Math.Min(e, 7 * 24 * 3600); // 上限 7 天(对象存储通常的最大值)

		// 临时链接也附带建议文件名,便于用户/客户端直接保存为“包名_版本.zip”。
		var fileName = await _downloads.GetSuggestedFileNameAsync(author, name, id, SourceId, HttpContext.RequestAborted);
		var url = await _storage.GetPresignedUrlAsync(resolution.ObjectKey!, expires, fileName, HttpContext.RequestAborted);
		if (string.IsNullOrEmpty(url))
			return NotFound(new { error = "Object not found in storage" });

		return DeliverLink(url, expires);
	}

	private IActionResult DeliverLink(string url, int? expiresIn)
	{
		var format = Request.Query["format"].ToString();
		if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
			return Ok(new { url, expires_in = expiresIn });

		return Redirect(url);
	}

	/// <summary>
	/// 直接文件路径(官方 as_dict 里的相对 url 就是 /uploads/xxx)。
	/// </summary>
	[HttpGet("/uploads/{**path}")]
	public Task<IActionResult> UploadsFile(string path) => ResolveAndDeliver("/uploads/" + path, forceProxy: true);

	/// <summary>
	/// 缩略图/封面图缓存端点。上游 as_dict 里的 thumbnail 会被改写为 {PublicBaseUrl}/thumbnails/...,
	/// 客户端/前端访问此处;首次回源官方并缓存到对象存储,之后走缓存。始终代理字节流(图片必须能直接显示,
	/// 不做跨域 302,避免前端 <img> 加载失败)。
	/// </summary>
	[HttpGet("/thumbnails/{**path}")]
	public async Task<IActionResult> Thumbnail(string path)
	{
		// 1) 已缓存的缩略图直接命中。
		//    对象存储不可用/超时(如跨境 R2 抖动)时不抛 500,降级继续走后续回退。
		//    注意:S3 SDK 的超时会抛 TaskCanceledException/OperationCanceledException;这与
		//    "客户端主动断开(RequestAborted)" 不同。仅当客户端真的断开时才放弃,否则一律降级。
		var localKey = "thumbnails/" + path;
		try
		{
			var localStream = await _storage.OpenReadAsync(localKey, HttpContext.RequestAborted);
			if (localStream is not null)
			{
				var info = await _storage.GetInfoAsync(localKey, HttpContext.RequestAborted);
				return CachedFile(localStream, info?.ContentType ?? "image/png");
			}
		}
		catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested)
		{
			// 客户端确实断开了:无需继续,直接结束。
			return new EmptyResult();
		}
		catch (Exception ex)
		{
			// 其余全部(含 S3 超时 TaskCanceledException / 网络异常)一律降级到后续回退,不抛 500。
			_logger.LogWarning(ex, "读取缓存缩略图失败(对象存储不可用/超时),降级回退 {Key}", localKey);
		}

		// 2) 尝试本地源图动态生成缩略图。path 形如 "{level}/{file}.{ext}"。
		//    源图位于 uploads/{file}.<原扩展名>;这里按去扩展名 + 已知图片扩展探测。
		//    对象存储不可用/超时不抛 500,降级继续。
		try
		{
			var parsed = ParseThumbnailPath(path);
			if (parsed is not null)
			{
				var (level, fileNoExt, outFormat) = parsed.Value;
				var sourceKey = await FindLocalSourceAsync(fileNoExt, HttpContext.RequestAborted);
				if (sourceKey is not null)
				{
					var thumb = await _thumbnails.GetOrCreateAsync(level, sourceKey, outFormat, HttpContext.RequestAborted);
					if (thumb.Success && thumb.Content is not null)
						return CachedFile(thumb.Content, thumb.ContentType ?? "image/webp");
				}
			}
		}
		catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested)
		{
			return new EmptyResult();
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "本地生成缩略图失败(对象存储不可用/超时),降级回退 {Path}", path);
		}

		// 3) 本地无 -> 回退到上游站点缓存。
		//    任何异常(TLS/超时/上游不可达)都降级为 404,保证首页不因单张图 500。
		try
		{
			var site = ResolveUpstream();
			if (site is null)
				return NotFound();

			var result = await _files.EnsureAndResolveAsync(site, "/thumbnails/" + path, UserAgent, HttpContext.RequestAborted);
			if (!result.Success)
				return NotFound();

			if (result.Content is not null)
				return CachedFile(result.Content, result.ContentType ?? "image/png");

			if (!string.IsNullOrEmpty(result.RedirectUrl))
				return Redirect(result.RedirectUrl);

			return NotFound();
		}
		catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested)
		{
			return new EmptyResult();
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "回源缩略图失败(上游不可达/超时),返回 404 {Path}", path);
			return NotFound();
		}
	}

	/// <summary>解析缩略图路径 "{level}/{file}.{ext}" -> (level, 去扩展名的文件名, 输出格式)。</summary>
	private static (int Level, string FileNoExt, string Format)? ParseThumbnailPath(string path)
	{
		var slash = path.IndexOf('/');
		if (slash <= 0) return null;
		if (!int.TryParse(path[..slash], out var level)) return null;

		var rest = path[(slash + 1)..];
		if (string.IsNullOrEmpty(rest)) return null;

		var dot = rest.LastIndexOf('.');
		var format = dot >= 0 ? rest[(dot + 1)..] : "webp";
		var fileNoExt = dot >= 0 ? rest[..dot] : rest;
		return (level, fileNoExt, format);
	}

	/// <summary>在 uploads/ 下按已知图片扩展名探测本地源图。</summary>
	private async Task<string?> FindLocalSourceAsync(string fileNoExt, CancellationToken ct)
	{
		foreach (var ext in new[] { "png", "jpg", "jpeg", "webp" })
		{
			var key = $"uploads/{fileNoExt}.{ext}";
			if (await _storage.ExistsAsync(key, ct))
				return key;
		}
		return null;
	}

	private FileStreamResult CachedFile(Stream content, string contentType)
	{
		// 浏览器/客户端可长时间缓存图片
		Response.Headers.CacheControl = "public, max-age=604800";
		return File(content, contentType);
	}

	private async Task<IActionResult> ResolveAndDeliver(string upstreamPath, bool forceProxy = false)
	{
		// url 可能是绝对(外链)或相对(/uploads/..)。仅镜像相对上传路径;外链直接跳转。
		if (upstreamPath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
			upstreamPath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
		{
			var pub = _options.PublicBaseUrl.TrimEnd('/');
			if (!string.IsNullOrEmpty(pub) && upstreamPath.StartsWith(pub, StringComparison.OrdinalIgnoreCase))
				upstreamPath = upstreamPath[pub.Length..];
			else
				return Redirect(upstreamPath);
		}

		// 先尝试本地对象存储(本地包上传文件直接存在 uploads/ 下)。
		var localKey = upstreamPath.TrimStart('/');
		var localStream = await _storage.OpenReadAsync(localKey, HttpContext.RequestAborted);
		if (localStream is not null)
		{
			var info = await _storage.GetInfoAsync(localKey, HttpContext.RequestAborted);
			return CachedFile(localStream, info?.ContentType ?? "application/octet-stream");
		}

		// 本地无 -> 回退到上游站点缓存。
		var site = ResolveUpstream();
		if (site is null)
			return NotFound();

		var result = await _files.EnsureAndResolveAsync(site, upstreamPath, UserAgent, HttpContext.RequestAborted);
		if (!result.Success)
			return StatusCode(result.UpstreamStatus == 0 ? 502 : result.UpstreamStatus);

		if (forceProxy && result.Content is not null)
			return CachedFile(result.Content, result.ContentType ?? "application/octet-stream");

		if (!string.IsNullOrEmpty(result.RedirectUrl))
			return Redirect(result.RedirectUrl);

		if (result.Content is not null)
			return File(result.Content, result.ContentType ?? "application/octet-stream");

		return StatusCode(502);
	}
}
