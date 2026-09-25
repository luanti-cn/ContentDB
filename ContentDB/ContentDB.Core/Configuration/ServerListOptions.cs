// ContentDB C# —— 服务器列表(/serverlists)配置。

namespace ContentDB.Core.Configuration;

public sealed class ServerListOptions
{
	public const string SectionName = "ServerList";

	/// <summary>上游官方 master(列表回源 + geoip)。</summary>
	public string Upstream { get; set; } = "https://servers.luanti.org";

	/// <summary>上游列表缓存秒数。</summary>
	public int TtlSeconds { get; set; } = 60;

	/// <summary>上游 geoip 不可用时的兜底大洲(站点主要用户所在)。</summary>
	public string GeoipFallbackContinent { get; set; } = "AS";

	/// <summary>本站收录服务器对外声明的协议范围。</summary>
	public int LocalProtoMin { get; set; } = 39;
	public int LocalProtoMax { get; set; } = 53;

	/// <summary>本站收录服务器对外声明的引擎版本与 gameid。</summary>
	public string LocalVersion { get; set; } = "5.18.0";
	public string LocalGameId { get; set; } = "luanticn";
}
