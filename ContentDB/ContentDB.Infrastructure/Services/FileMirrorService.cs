// ContentDB C# Mirror
// 文件懒镜像:首次未命中时同步从官方拉取写入对象存储,然后 302 到公共 URL(或代理流)。

using ContentDB.Core.Abstractions;
using ContentDB.Core.Configuration;
using ContentDB.Core.Entities;
using ContentDB.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// 多源文件镜像:记录键与对象 key 均按 source 分区,避免多个上游站点文件名冲突。

using System.Collections.Concurrent;

namespace ContentDB.Infrastructure.Services;

public sealed class FileMirrorService : IFileMirrorService
{
	private readonly MirrorDbContext _db;
	private readonly IUpstreamClient _upstream;
	private readonly IObjectStorage _storage;
	private readonly MirrorOptions _options;
	private readonly StorageOptions _storageOptions;
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly ILogger<FileMirrorService> _logger;

	// single-flight:同一文件的并发请求只放一个去回源,其余等锁后直接读已落地对象,
	// 避免热门包首次被访问时触发 N 份并发跨境下载。
	private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new();

	public FileMirrorService(
		MirrorDbContext db,
		IUpstreamClient upstream,
		IObjectStorage storage,
		IOptions<MirrorOptions> options,
		IOptions<StorageOptions> storageOptions,
		IServiceScopeFactory scopeFactory,
		ILogger<FileMirrorService> logger)
	{
		_db = db;
		_upstream = upstream;
		_storage = storage;
		_options = options.Value;
		_storageOptions = storageOptions.Value;
		_scopeFactory = scopeFactory;
		_logger = logger;
	}

	/// <summary>记录键按 source 分区:[sourceId]path。</summary>
	private static string RecordKey(string sourceId, string upstreamPath) => $"[{sourceId}]{upstreamPath}";

	private static SemaphoreSlim Gate(string recordKey)
		=> Gates.GetOrAdd(recordKey, _ => new SemaphoreSlim(1, 1));

	public async Task<FileResolveResult> EnsureAndResolveAsync(
		UpstreamSiteOptions source,
		string upstreamPath,
		string? userAgent = null,
		CancellationToken ct = default)
	{
		var recordKey = RecordKey(source.Id, upstreamPath);
		var gate = Gate(recordKey);
		await gate.WaitAsync(ct);
		try
		{
			return await EnsureAndResolveCoreAsync(source, upstreamPath, recordKey, userAgent, ct);
		}
		finally
		{
			gate.Release();
		}
	}

	private async Task<FileResolveResult> EnsureAndResolveCoreAsync(
		UpstreamSiteOptions source,
		string upstreamPath,
		string recordKey,
		string? userAgent,
		CancellationToken ct)
	{
		// 1. 已镜像?(按 [source]path 查记录,拿到干净的对象 key)直接交付。
		var record = await _db.MirroredFiles.FirstOrDefaultAsync(x => x.UpstreamPath == recordKey, ct);
		if (record is { IsStored: true } && await _storage.ExistsAsync(record.ObjectKey, ct))
		{
			return await DeliverAsync(record.ObjectKey, ct);
		}

		// 2. 未命中:同步从该上游站拉取(跟随 302 到实际文件)。
		var file = await _upstream.GetFileAsync(source.BaseUrl, upstreamPath, userAgent, ct);
		if (!file.Success() || file.Content is null)
		{
			_logger.LogWarning("回源文件失败 [{Source}] {Path} -> {Status}", source.Id, upstreamPath, file.StatusCode);
			return new FileResolveResult(false, null, null, null, null, file.StatusCode);
		}

		// 3. 依据最终解析到的真实文件路径生成干净的对象 key(按 source 分区)。
		var objectKey = DeriveObjectKey(source.Id, upstreamPath, file.FinalUrl);

		// 4. 落地到内存并校验内容确为 zip;避免把上游错误页(403/HTML)当作发布文件缓存。
		byte[] bytes;
		await using (var buffer = new MemoryStream())
		{
			await file.Content.CopyToAsync(buffer, ct);
			bytes = buffer.ToArray();
		}
		file.Content.Dispose();

		bool isZipKey = objectKey.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
		if (isZipKey && !LooksLikeZip(bytes))
		{
			_logger.LogWarning("回源内容不是有效 zip(可能是错误页/权限问题) {Path} finalUrl={FinalUrl} ct={ContentType} size={Size}",
				upstreamPath, file.FinalUrl, file.ContentType, bytes.Length);
			// 不缓存,返回 502 让客户端明确失败,而不是拿到坏存档报“找不到有效的 Mod/子游戏”。
			return new FileResolveResult(false, null, null, null, null, 502);
		}

		// 5. 边下边存(proxy-through):首次请求不等 COS 上传完成,
		//    直接把已在内存、且校验为 zip 的字节流回给客户端(避免 Luanti 下载超时);
		//    同时在后台异步把同一份字节写入 COS,供后续请求走 302 预签名 URL。
		var contentType = file.ContentType ?? GuessContentType(objectKey);
		_ = Task.Run(() => StoreInBackgroundAsync(recordKey, objectKey, bytes, contentType));

		return new FileResolveResult(
			Success: true,
			RedirectUrl: null,
			Content: new MemoryStream(bytes, writable: false),
			ContentType: contentType,
			ContentLength: bytes.LongLength,
			UpstreamStatus: 200);
	}

	public async Task<string?> EnsureStoredAndGetKeyAsync(
		UpstreamSiteOptions source,
		string upstreamPath,
		string? userAgent = null,
		CancellationToken ct = default)
	{
		var recordKey = RecordKey(source.Id, upstreamPath);
		var gate = Gate(recordKey);
		await gate.WaitAsync(ct);
		try
		{
			return await EnsureStoredAndGetKeyCoreAsync(source, upstreamPath, recordKey, userAgent, ct);
		}
		finally
		{
			gate.Release();
		}
	}

	private async Task<string?> EnsureStoredAndGetKeyCoreAsync(
		UpstreamSiteOptions source,
		string upstreamPath,
		string recordKey,
		string? userAgent,
		CancellationToken ct)
	{
		// 1. 已镜像且对象仍在桶中 -> 直接返回 key。
		var record = await _db.MirroredFiles.FirstOrDefaultAsync(x => x.UpstreamPath == recordKey, ct);
		if (record is { IsStored: true } && await _storage.ExistsAsync(record.ObjectKey, ct))
			return record.ObjectKey;

		// 2. 未命中:同步回源。
		var file = await _upstream.GetFileAsync(source.BaseUrl, upstreamPath, userAgent, ct);
		if (!file.Success() || file.Content is null)
		{
			_logger.LogWarning("回源文件失败(临时链接) [{Source}] {Path} -> {Status}", source.Id, upstreamPath, file.StatusCode);
			return null;
		}

		var objectKey = DeriveObjectKey(source.Id, upstreamPath, file.FinalUrl);

		byte[] bytes;
		await using (var buffer = new MemoryStream())
		{
			await file.Content.CopyToAsync(buffer, ct);
			bytes = buffer.ToArray();
		}
		file.Content.Dispose();

		bool isZipKey = objectKey.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
		if (isZipKey && !LooksLikeZip(bytes))
		{
			_logger.LogWarning("回源内容不是有效 zip(临时链接) {Path} size={Size}", upstreamPath, bytes.Length);
			return null;
		}

		var contentType = file.ContentType ?? GuessContentType(objectKey);

		// 3. **同步**写入对象存储并落库(临时链接必须指向已存在对象)。
		await StoreInBackgroundAsync(recordKey, objectKey, bytes, contentType);
		return objectKey;
	}

	/// <summary>把首次回源的字节写入对象存储并落库,失败仅记日志(下次会重试回源)。recordKey 已按 source 分区。</summary>
	private async Task StoreInBackgroundAsync(string recordKey, string objectKey, byte[] bytes, string contentType)
	{
		// 后台任务脱离请求作用域,需自建 DbContext 作用域 —— 这里通过工厂委托注入。
		try
		{
			await using (var upload = new MemoryStream(bytes, writable: false))
			{
				await _storage.PutAsync(objectKey, upload, contentType, CancellationToken.None);
			}

			await using var scope = _scopeFactory.CreateAsyncScope();
			var db = scope.ServiceProvider.GetRequiredService<MirrorDbContext>();

			var now = DateTimeOffset.UtcNow;
			var record = await db.MirroredFiles.FirstOrDefaultAsync(x => x.UpstreamPath == recordKey);
			if (record is null)
			{
				record = new MirroredFile
				{
					UpstreamPath = recordKey,
					ObjectKey = objectKey,
					FirstSeenAt = now,
				};
				db.MirroredFiles.Add(record);
			}
			record.ObjectKey = objectKey;
			record.IsStored = true;
			record.StoredAt = now;
			record.ContentType = contentType;
			record.SizeBytes = bytes.LongLength;
			await db.SaveChangesAsync();

			_logger.LogInformation("后台已缓存文件到对象存储 {Key} ({Size} bytes)", objectKey, bytes.LongLength);
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "后台缓存文件失败 {Key},下次请求将重试回源", objectKey);
		}
	}

	/// <summary>
	/// 依据最终解析到的真实文件 URL 推导干净的对象 key。
	/// 优先用 finalUrl 的路径(通常是 /uploads/&lt;random&gt;.zip);
	/// 若 finalUrl 缺失或指回 download 端点,则回退为按 release 生成带 .zip 的稳定 key。
	/// </summary>
	private static string DeriveObjectKey(string sourceId, string upstreamPath, string? finalUrl)
	{
		// 按 source 分区,避免不同上游站点的同名文件互相覆盖。
		var prefix = $"mirror/{sourceId}/";

		if (!string.IsNullOrEmpty(finalUrl) && Uri.TryCreate(finalUrl, UriKind.Absolute, out var uri))
		{
			var path = uri.AbsolutePath.TrimStart('/');
			if (path.Length > 0 && !path.EndsWith('/') && Path.HasExtension(path))
				return prefix + path; // 如 mirror/luanti-org/uploads/abcd1234.zip
		}

		// 回退:把 download 端点路径规约为带 .zip 的可用 key
		var key = upstreamPath.TrimStart('/').TrimEnd('/');
		key = key.Replace('/', '_');
		if (!key.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
			key += ".zip";
		return prefix + key;
	}

	private static bool LooksLikeZip(byte[] bytes)
	{
		if (bytes.Length < 4) return false;
		// PK\x03\x04(普通)/ PK\x05\x06(空)/ PK\x07\x08(跨段)
		return bytes[0] == 0x50 && bytes[1] == 0x4B
			&& (bytes[2] == 0x03 || bytes[2] == 0x05 || bytes[2] == 0x07);
	}

	private static string GuessContentType(string key)
	{
		var lower = key.ToLowerInvariant();
		if (lower.EndsWith(".zip")) return "application/zip";
		if (lower.EndsWith(".png")) return "image/png";
		if (lower.EndsWith(".jpg") || lower.EndsWith(".jpeg")) return "image/jpeg";
		if (lower.EndsWith(".webp")) return "image/webp";
		return "application/octet-stream";
	}

	private async Task<FileResolveResult> DeliverAsync(string objectKey, CancellationToken ct)
	{
		// ProxyDownloads=true(默认):由镜像自身代理对象存储字节流,同源交付,
		// 规避 Luanti 对跨主机 302 到对象存储/预签名 URL 的兼容问题。
		if (!_storageOptions.ProxyDownloads)
		{
			// 公共读桶拼接 URL 或私有桶预签名 URL
			var downloadUrl = await _storage.GetDownloadUrlAsync(objectKey, ct);
			if (!string.IsNullOrEmpty(downloadUrl))
			{
				return new FileResolveResult(true, downloadUrl, null, null, null, 200);
			}
		}

		// 由镜像自身代理对象存储的流。
		var info = await _storage.GetInfoAsync(objectKey, ct);
		var stream = await _storage.OpenReadAsync(objectKey, ct);
		return new FileResolveResult(true, null, stream, info?.ContentType, info?.Size, 200);
	}
}

file static class FileResolveExtensions
{
	public static bool Success(this UpstreamFileResponse r) => r.StatusCode is >= 200 and < 300;
}
