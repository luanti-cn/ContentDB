// ContentDB C# Mirror
// IObjectStorage 的 S3 兼容实现,基于 AWSSDK.S3。
// 可通过 StorageOptions.ServiceUrl + ForcePathStyle 对接阿里云 OSS / 腾讯云 COS / MinIO。

using Amazon.S3;
using Amazon.S3.Model;
using ContentDB.Core.Abstractions;
using ContentDB.Core.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ContentDB.Infrastructure.Storage;

public sealed class S3ObjectStorage : IObjectStorage
{
	private readonly IAmazonS3 _s3;
	private readonly StorageOptions _options;
	private readonly ILogger<S3ObjectStorage> _logger;

	public S3ObjectStorage(IAmazonS3 s3, IOptions<StorageOptions> options, ILogger<S3ObjectStorage> logger)
	{
		_s3 = s3;
		_options = options.Value;
		_logger = logger;
	}

	private static string Normalize(string key) => key.TrimStart('/');

	public async Task<bool> ExistsAsync(string key, CancellationToken ct = default)
	{
		return await GetInfoAsync(key, ct) is not null;
	}

	public async Task<StoredObjectInfo?> GetInfoAsync(string key, CancellationToken ct = default)
	{
		key = Normalize(key);
		try
		{
			var meta = await _s3.GetObjectMetadataAsync(new GetObjectMetadataRequest
			{
				BucketName = _options.Bucket,
				Key = key
			}, ct);

			return new StoredObjectInfo(
				key,
				meta.ContentLength,
				meta.Headers.ContentType,
				meta.LastModified);
		}
		catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
		{
			return null;
		}
	}

	public async Task<StoredObjectInfo> PutAsync(string key, Stream content, string? contentType, CancellationToken ct = default)
	{
		key = Normalize(key);

		var request = new PutObjectRequest
		{
			BucketName = _options.Bucket,
			Key = key,
			InputStream = content,
			AutoCloseStream = false,
			DisablePayloadSigning = true // 兼容部分非 AWS 服务(如 MinIO over HTTP)
		};

		if (!string.IsNullOrEmpty(contentType))
			request.ContentType = contentType;

		await _s3.PutObjectAsync(request, ct);

		long size = content.CanSeek ? content.Length : 0;
		_logger.LogInformation("已上传对象 {Key} ({Size} bytes) 到桶 {Bucket}", key, size, _options.Bucket);

		return new StoredObjectInfo(key, size, contentType, DateTimeOffset.UtcNow);
	}

	public async Task<Stream?> OpenReadAsync(string key, CancellationToken ct = default)
	{
		key = Normalize(key);
		try
		{
			var response = await _s3.GetObjectAsync(new GetObjectRequest
			{
				BucketName = _options.Bucket,
				Key = key
			}, ct);

			return response.ResponseStream;
		}
		catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
		{
			return null;
		}
	}

	public async Task DeleteAsync(string key, CancellationToken ct = default)
	{
		key = Normalize(key);
		await _s3.DeleteObjectAsync(new DeleteObjectRequest
		{
			BucketName = _options.Bucket,
			Key = key
		}, ct);
	}

	public string? GetPublicUrl(string key)
	{
		if (_options.UsePresignedUrls || string.IsNullOrEmpty(_options.PublicUrlBase))
			return null;

		key = Normalize(key);
		return $"{_options.PublicUrlBase.TrimEnd('/')}/{key}";
	}

	public async Task<string?> GetDownloadUrlAsync(string key, CancellationToken ct = default)
	{
		key = Normalize(key);

		// 私有桶:生成预签名 GET URL
		if (_options.UsePresignedUrls)
			return await CreatePresignedUrlAsync(key, _options.PresignedUrlExpirySeconds);

		// 公共读桶
		return GetPublicUrl(key);
	}

	public async Task<string?> GetPresignedUrlAsync(
		string key,
		int? expirySeconds = null,
		string? downloadFileName = null,
		CancellationToken ct = default)
	{
		key = Normalize(key);

		// 该端点面向“获取临时链接” / Web 直链场景,强制生成预签名 URL,无视 ProxyDownloads/UsePresignedUrls。
		// 生成前确认对象存在,避免签发指向不存在对象的链接。
		if (!await ExistsAsync(key, ct))
			return null;

		var expiry = expirySeconds ?? _options.PresignedUrlExpirySeconds;
		return await CreatePresignedUrlAsync(key, expiry, downloadFileName);
	}

	private async Task<string> CreatePresignedUrlAsync(string key, int expirySeconds, string? downloadFileName = null)
	{
		var request = new GetPreSignedUrlRequest
		{
			BucketName = _options.Bucket,
			Key = key,
			Verb = HttpVerb.GET,
			Expires = DateTime.UtcNow.AddSeconds(expirySeconds),
		};

		// 指定下载文件名:签名进 response-content-disposition,浏览器据此保存正确文件名。
		if (!string.IsNullOrEmpty(downloadFileName))
		{
			var encoded = Uri.EscapeDataString(downloadFileName);
			request.ResponseHeaderOverrides.ContentDisposition =
				$"attachment; filename=\"{downloadFileName}\"; filename*=UTF-8''{encoded}";
		}

		var url = await _s3.GetPreSignedURLAsync(request);

		// 可选:把签名 URL 的 host 替换为对外可达主机(需保证签名以对外 host 计算,
		// 通常直接把 ServiceUrl 设为对外地址更稳妥;此处仅在明确配置时替换)。
		if (!string.IsNullOrEmpty(_options.PresignedPublicHost))
			url = ReplaceHost(url, _options.PresignedPublicHost);

		return url;
	}

	private static string ReplaceHost(string url, string publicHost)
	{
		try
		{
			var original = new Uri(url);
			var target = new Uri(publicHost.TrimEnd('/'));
			var builder = new UriBuilder(original)
			{
				Scheme = target.Scheme,
				Host = target.Host,
				Port = target.IsDefaultPort ? -1 : target.Port,
			};
			return builder.Uri.ToString();
		}
		catch
		{
			return url;
		}
	}
}
