// ContentDB C# —— LuantiCN 更新检查信息(/release_info.json)的配置。

namespace ContentDB.Core.Configuration;

public sealed class ReleaseChannelInfo
{
	/// <summary>最新版本号,如 "5.18.0"。</summary>
	public string Version { get; set; } = "";

	/// <summary>版本数字:(主版本 * 1000 + 次版本) * 1000 + 修订,客户端据此比较新旧。</summary>
	public long VersionCode { get; set; }

	/// <summary>"访问网站"按钮打开的下载页地址。</summary>
	public string? Url { get; set; }
}

public sealed class UpdateInfoOptions
{
	public const string SectionName = "UpdateInfo";

	public ReleaseChannelInfo? Latest { get; set; }
}
