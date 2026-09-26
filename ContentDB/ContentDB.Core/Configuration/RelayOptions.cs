// ContentDB C# —— 中继(Relay)对接配置
// 支持多节点横向扩展:Nodes 数组按房间分片,单节点场景可继续用平铺字段(自动合成 default 节点)。

namespace ContentDB.Core.Configuration;

public sealed class RelayOptions
{
	public const string SectionName = "Relay";

	/// <summary>是否启用中继候选。</summary>
	public bool Enabled { get; set; }

	// ---- 单节点平铺字段(向后兼容;Nodes 为空时合成名为 default 的节点) ----

	/// <summary>中继控制面地址,如 http://127.0.0.1:5180。</summary>
	public string BaseUrl { get; set; } = "";

	/// <summary>内部密钥(X-Relay-Secret 头;与 Relay.InternalSecret 一致)。</summary>
	public string Secret { get; set; } = "";

	/// <summary>对外公布的中继地址(客户端连接用,如 relay.luanti.cn)。</summary>
	public string PublicHost { get; set; } = "";

	// ---- 多节点(横向扩展;非空时忽略上面的平铺字段) ----

	public List<RelayNodeOptions> Nodes { get; set; } = [];
}

public sealed class RelayNodeOptions
{
	/// <summary>节点名(房间路由标识,如 relay-bj-1)。</summary>
	public string Name { get; set; } = "";

	/// <summary>控制面地址,如 http://10.0.0.5:5180。</summary>
	public string BaseUrl { get; set; } = "";

	/// <summary>该节点的内部密钥(为空用顶层 Secret)。</summary>
	public string? Secret { get; set; }

	/// <summary>对外公布的地址(客户端连接用,如 relay-bj.luanti.cn)。</summary>
	public string PublicHost { get; set; } = "";
}
