// ContentDB C# —— 多源解析器
// 提供本站信息、所有启用的上游站点,以及按 baseUrl 反查 source(用于把官方 download URL 归属到某站)。

using ContentDB.Core.Configuration;

namespace ContentDB.Core.Abstractions;

public interface ISourceResolver
{
	/// <summary>本站(LOCAL)配置。</summary>
	LocalSiteOptions Local { get; }

	/// <summary>所有启用的上游站点(按 Order 升序)。</summary>
	IReadOnlyList<UpstreamSiteOptions> Upstreams { get; }

	/// <summary>按 id 查上游;找不到返回 null。</summary>
	UpstreamSiteOptions? FindById(string id);

	/// <summary>
	/// 默认上游(Order 最小的启用上游)。用于未显式指定 source 的旧客户端下载兼容。
	/// 无启用上游时返回 null。
	/// </summary>
	UpstreamSiteOptions? Default { get; }
}
