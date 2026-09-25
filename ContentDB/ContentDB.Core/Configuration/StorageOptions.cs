// ContentDB C# Mirror
// S3 兼容对象存储配置。可对接 AWS S3 / 阿里云 OSS / 腾讯云 COS / MinIO 等。

namespace ContentDB.Core.Configuration;

public sealed class StorageOptions
{
	public const string SectionName = "Storage";

	/// <summary>S3 兼容服务的自定义 endpoint(如 https://oss-cn-hangzhou.aliyuncs.com 或 http://minio:9000)。留空则用 AWS 默认。</summary>
	public string? ServiceUrl { get; set; }

	/// <summary>区域(如 cn-hangzhou、us-east-1)。部分兼容服务可留 us-east-1。</summary>
	public string Region { get; set; } = "us-east-1";

	public string AccessKey { get; set; } = "";

	public string SecretKey { get; set; } = "";

	/// <summary>存储桶名。</summary>
	public string Bucket { get; set; } = "contentdb";

	/// <summary>是否使用 path-style 访问(MinIO / 部分自建服务需要 true)。</summary>
	public bool ForcePathStyle { get; set; } = true;

	/// <summary>
	/// S3 请求超时(秒)。跨境访问 Cloudflare R2 等 endpoint 延迟较高时,默认超时可能过短导致
	/// TaskCanceledException;适当调大(如 30~60)。&lt;=0 表示用 SDK 默认。
	/// </summary>
	public int TimeoutSeconds { get; set; } = 30;

	/// <summary>S3 最大重试次数(默认 SDK 值偏多会放大延迟;跨境场景可设 2)。&lt;0 表示用 SDK 默认。</summary>
	public int MaxErrorRetry { get; set; } = 2;

	/// <summary>
	/// 对外暴露文件的公共基础 URL(CDN 域名或 bucket 公网域名)。
	/// 仅在 UsePresignedUrls=false(公共读桶)时用于拼接 {PublicUrlBase}/{objectKey}。
	/// 留空则回退到通过本镜像自身代理文件流。
	/// </summary>
	public string? PublicUrlBase { get; set; }

	/// <summary>
	/// 私有桶:是否为下载生成预签名(presigned)GET URL。
	/// true 时 download 端点 302 到带签名、限时有效的 URL(不依赖桶公共读)。
	/// </summary>
	public bool UsePresignedUrls { get; set; }

	/// <summary>预签名 URL 有效期(秒),默认 1 小时。</summary>
	public int PresignedUrlExpirySeconds { get; set; } = 3600;

	/// <summary>
	/// 预签名 URL 对外基础地址(可选)。当客户端访问的域名(CDN/公网)与
	/// 生成签名所用的内部 endpoint 不同,可在此设置对外主机以替换签名 URL 的 host 部分。
	/// 注意:替换 host 后签名仍有效需保证签名计算所用 host 与对外 host 一致,
	/// 否则应直接把 ServiceUrl 设为对外可达地址。留空表示不替换。
	/// </summary>
	public string? PresignedPublicHost { get; set; }

	/// <summary>
	/// 下载交付方式。true(默认):由本镜像自身把 zip 字节流回传给客户端(同源、最稳,
	/// 彻底规避 Luanti 客户端对跨主机 302 到对象存储/预签名 URL 的兼容问题);
	/// false:302 重定向到对象存储的公共/预签名 URL(省镜像带宽,但依赖客户端跟随跨域重定向)。
	/// </summary>
	public bool ProxyDownloads { get; set; } = true;
}
