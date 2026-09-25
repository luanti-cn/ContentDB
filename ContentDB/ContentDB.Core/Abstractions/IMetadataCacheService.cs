// ContentDB C# Mirror
// 元数据懒缓存服务抽象:命中返回本地,未命中/过期回源,回源失败可降级返回陈旧缓存。
// 多源:按上游站点(source)分别缓存与回源。

using ContentDB.Core.Configuration;

namespace ContentDB.Core.Abstractions;

public sealed record CachedApiResult(int StatusCode, string Body, string? ContentType);

public interface IMetadataCacheService
{
	/// <summary>
	/// 获取某个上游站点的 API 端点响应(懒缓存)。
	/// </summary>
	/// <param name="source">上游站点(提供 baseUrl / id,用于回源与缓存分区)</param>
	/// <param name="relativePathAndQuery">上游相对路径含查询,如 "/api/packages/?type=mod"</param>
	/// <param name="ttlSeconds">该端点的缓存 TTL(秒)</param>
	/// <param name="acceptLanguage">客户端 Accept-Language(参与缓存键与回源)</param>
	/// <param name="userAgent">客户端 UA(回源透传)</param>
	/// <param name="rewriteUrls">是否把响应中的上游绝对 URL 改写为镜像域名</param>
	Task<CachedApiResult> GetOrFetchAsync(
		UpstreamSiteOptions source,
		string relativePathAndQuery,
		int ttlSeconds,
		string? acceptLanguage = null,
		string? userAgent = null,
		bool rewriteUrls = true,
		CancellationToken ct = default);
}
