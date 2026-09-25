// ContentDB C# Mirror
// 镜像服务的强类型配置。绑定自 appsettings.json 的 "Mirror" / "Storage" 节。

namespace ContentDB.Core.Configuration;

/// <summary>
/// 上游回源与缓存行为配置。
/// </summary>
public sealed class MirrorOptions
{
	public const string SectionName = "Mirror";

	/// <summary>[已弃用] 单源时代的上游地址;多源改用 SourceSites.Upstreams。保留仅为兼容旧配置。</summary>
	public string UpstreamBaseUrl { get; set; } = "https://content.luanti.org";

	/// <summary>本镜像对外公开的基础 URL,用于改写返回给客户端的绝对 URL(thumbnail/screenshots 等)。</summary>
	public string PublicBaseUrl { get; set; } = "http://localhost:5175";

	/// <summary>回源 HTTP 超时(秒)。</summary>
	public int UpstreamTimeoutSeconds { get; set; } = 30;

	/// <summary>回源失败时是否允许返回过期的本地缓存(降级可用性)。</summary>
	public bool ServeStaleOnUpstreamError { get; set; } = true;

	/// <summary>各类元数据缓存 TTL(秒)。与官方 @cached 时长对齐。</summary>
	public MetadataTtlOptions MetadataTtlSeconds { get; set; } = new();

	/// <summary>回源时透传给上游的 User-Agent(官方靠 UA 判定 Luanti 客户端)。</summary>
	public bool ForwardUserAgent { get; set; } = true;
}

public sealed class MetadataTtlOptions
{
	public int Packages { get; set; } = 300;
	public int Updates { get; set; } = 300;
	public int ForClient { get; set; } = 300;
	public int Dependencies { get; set; } = 300;
	public int Homepage { get; set; } = 300;
	public int Tags { get; set; } = 3600;
	public int Licenses { get; set; } = 3600;
	public int ContentWarnings { get; set; } = 3600;
	public int Default { get; set; } = 300;
}
