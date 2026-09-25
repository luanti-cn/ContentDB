// ContentDB C# Mirror
// 上游回源客户端抽象。支持多源:每次调用指定上游站点的 baseUrl。

namespace ContentDB.Core.Abstractions;

public sealed record UpstreamJsonResponse(int StatusCode, string Body, string? ContentType);

public sealed record UpstreamFileResponse(
	int StatusCode,
	Stream? Content,
	string? ContentType,
	long? ContentLength,
	string? FinalUrl = null);

public interface IUpstreamClient
{
	/// <summary>
	/// 回源 GET 一个 API JSON 端点。
	/// </summary>
	/// <param name="baseUrl">上游站点基础地址,如 https://content.luanti.org</param>
	/// <param name="relativePathAndQuery">形如 "/api/packages/?type=mod"</param>
	/// <param name="extraHeaders">可选附加请求头(如转发客户端 IP 的 X-Forwarded-For)。</param>
	Task<UpstreamJsonResponse> GetJsonAsync(
		string baseUrl,
		string relativePathAndQuery,
		string? acceptLanguage = null,
		string? userAgent = null,
		CancellationToken ct = default,
		IReadOnlyDictionary<string, string>? extraHeaders = null);

	/// <summary>
	/// 回源下载一个文件(release zip / 截图)。
	/// </summary>
	/// <param name="baseUrl">上游站点基础地址</param>
	/// <param name="absoluteOrRelativeUrl">官方返回的相对路径(/uploads/xxx.zip)或绝对 URL</param>
	Task<UpstreamFileResponse> GetFileAsync(
		string baseUrl,
		string absoluteOrRelativeUrl,
		string? userAgent = null,
		CancellationToken ct = default);
}
