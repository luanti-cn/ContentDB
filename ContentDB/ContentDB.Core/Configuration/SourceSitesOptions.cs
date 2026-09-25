// ContentDB C# —— 多源站点配置
// 支持"本站"(LOCAL)+ 多个上游镜像站(如 Luanti.org 官方、其他社区镜像)。
// 每个站点有稳定 id、显示名、简介前缀标签、基础 URL、启用开关、排序权重。

namespace ContentDB.Core.Configuration;

public sealed class SourceSitesOptions
{
	public const string SectionName = "SourceSites";

	/// <summary>本站(自主运营)配置。</summary>
	public LocalSiteOptions Local { get; set; } = new();

	/// <summary>上游站点列表(可配置多个)。按 Order 升序排列;同源内部按时间倒序。</summary>
	public List<UpstreamSiteOptions> Upstreams { get; set; } = new();

	/// <summary>返回所有启用的上游,按 Order 升序。</summary>
	public IEnumerable<UpstreamSiteOptions> EnabledUpstreams()
		=> Upstreams.Where(u => u.Enabled).OrderBy(u => u.Order);

	/// <summary>按 id 查找上游站点(不含本站)。</summary>
	public UpstreamSiteOptions? FindUpstream(string id)
		=> Upstreams.FirstOrDefault(u => string.Equals(u.Id, id, StringComparison.OrdinalIgnoreCase));
}

public sealed class LocalSiteOptions
{
	/// <summary>本站 id(用于 source 标记)。</summary>
	public string Id { get; set; } = "local";

	/// <summary>本站显示名(Web UI 徽章)。</summary>
	public string Name { get; set; } = "本站";

	/// <summary>Luanti 客户端简介前缀标签,如 [Luanti.cn]。空则不加前缀。</summary>
	public string Label { get; set; } = "[Luanti.cn]";
}

public sealed class UpstreamSiteOptions
{
	/// <summary>站点 id(稳定标识,用于 source 路由与标记),如 "luanti-org"。</summary>
	public string Id { get; set; } = "";

	/// <summary>显示名(Web UI 徽章),如 "Luanti.org"。</summary>
	public string Name { get; set; } = "";

	/// <summary>Luanti 客户端简介前缀标签,如 [Luanti.org]。空则不加前缀。</summary>
	public string Label { get; set; } = "";

	/// <summary>该站点的基础 URL(回源目标),如 https://content.luanti.org。</summary>
	public string BaseUrl { get; set; } = "";

	/// <summary>是否启用。</summary>
	public bool Enabled { get; set; } = true;

	/// <summary>排序权重(越小越靠前)。本站恒在所有上游之前。</summary>
	public int Order { get; set; } = 100;

	/// <summary>回源超时(秒);<=0 用全局默认。</summary>
	public int TimeoutSeconds { get; set; } = 0;
}
