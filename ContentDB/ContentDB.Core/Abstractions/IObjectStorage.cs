// ContentDB C# Mirror
// 对象存储抽象。屏蔽底层是 S3 / OSS / COS / MinIO 还是本地磁盘。

namespace ContentDB.Core.Abstractions;

public sealed record StoredObjectInfo(string Key, long Size, string? ContentType, DateTimeOffset? LastModified);

public interface IObjectStorage
{
	/// <summary>对象是否已存在。</summary>
	Task<bool> ExistsAsync(string key, CancellationToken ct = default);

	/// <summary>获取对象元信息;不存在返回 null。</summary>
	Task<StoredObjectInfo?> GetInfoAsync(string key, CancellationToken ct = default);

	/// <summary>上传对象(覆盖同名)。返回存储后的对象信息。</summary>
	Task<StoredObjectInfo> PutAsync(string key, Stream content, string? contentType, CancellationToken ct = default);

	/// <summary>读取对象内容流;不存在返回 null。</summary>
	Task<Stream?> OpenReadAsync(string key, CancellationToken ct = default);

	/// <summary>删除对象。</summary>
	Task DeleteAsync(string key, CancellationToken ct = default);

	/// <summary>
	/// 获取对象的公开访问 URL(仅公共读桶,拼接 PublicUrlBase)。
	/// 私有桶或未配置公共 URL 时返回 null。
	/// </summary>
	string? GetPublicUrl(string key);

	/// <summary>
	/// 获取用于 302 重定向的下载 URL:
	/// - 公共读桶且配置了 PublicUrlBase:返回拼接的公共 URL;
	/// - 私有桶且 UsePresignedUrls=true:返回限时有效的预签名 GET URL;
	/// - 其余情况返回 null,调用方应回退为通过镜像自身代理文件流。
	/// </summary>
	Task<string?> GetDownloadUrlAsync(string key, CancellationToken ct = default);

	/// <summary>
	/// 强制生成一个限时有效的预签名 GET URL(不受 ProxyDownloads / UsePresignedUrls 开关影响)。
	/// 专供“获取临时链接” / Web 直链下载端点使用。对象不存在返回 null。
	/// </summary>
	/// <param name="expirySeconds">有效期(秒);null 表示使用配置默认值。</param>
	/// <param name="downloadFileName">
	/// 可选:附加 response-content-disposition 覆盖,使对象存储返回
	/// Content-Disposition: attachment; filename="..." ,浏览器据此保存为期望文件名。
	/// </param>
	Task<string?> GetPresignedUrlAsync(
		string key,
		int? expirySeconds = null,
		string? downloadFileName = null,
		CancellationToken ct = default);
}
