// ContentDB C# Mirror
// 把上游 JSON 响应体里的官方绝对 URL 改写为镜像自身的公开 URL。
// 官方在 as_dict 等处会拼接 BASE_URL(如 https://content.luanti.org)+ /uploads/... 或 /thumbnails/...
// 客户端拿到后直接访问这些 URL,因此必须改写成镜像域名,才能把流量导向国内。

namespace ContentDB.Core.Services;

public sealed class UrlRewriter
{
	private readonly string _upstreamBase;
	private readonly string _publicBase;

	public UrlRewriter(string upstreamBaseUrl, string publicBaseUrl)
	{
		_upstreamBase = upstreamBaseUrl.TrimEnd('/');
		_publicBase = publicBaseUrl.TrimEnd('/');
	}

	/// <summary>
	/// 简单而稳健:直接把响应体里出现的上游基础 URL 文本替换为镜像基础 URL。
	/// 覆盖 thumbnail / screenshots / url / previous / next 等所有绝对 URL 字段。
	/// </summary>
	public string Rewrite(string body)
	{
		if (string.IsNullOrEmpty(body) || _upstreamBase == _publicBase)
			return body;

		return body.Replace(_upstreamBase, _publicBase, StringComparison.OrdinalIgnoreCase);
	}
}
