// ContentDB C# —— 下载解析服务抽象
// 本地包发布 -> 本地/对象存储文件; 上游包发布 -> 走上游代理缓存(FileMirrorService)。

namespace ContentDB.Core.Abstractions;

public sealed record DownloadResolution(
	bool Found,
	string? RedirectUrl,   // 302 目标(本地对象存储公共 URL 或上游代理结果)
	Stream? Content,        // 或直接代理文件流
	string? ContentType,
	long? ContentLength,
	int StatusCode,
	string? SuggestedFileName = null); // 建议的下载文件名,如 mymod_1.2.3.zip

/// <summary>某个 release 在对象存储中的定位结果。</summary>
public sealed record ObjectKeyResolution(
	bool Found,
	string? ObjectKey,     // 对象存储 key(已确保入库/入桶)
	string? ExternalUrl,   // 若 release 为外链(非对象存储),直接给出外链
	int StatusCode);

public interface IDownloadService
{
	/// <summary>
	/// 解析 /packages/{author}/{name}/releases/{id}/download/。
	/// 先查本地库;命中则交付本地文件并计数;未命中回退到指定上游(sourceId)或默认上游代理缓存。
	/// </summary>
	Task<DownloadResolution> ResolveReleaseDownloadAsync(
		string author, string name, int releaseId,
		string clientIp, string? userAgent, string? reason,
		string? sourceId = null,
		CancellationToken ct = default);

	/// <summary>
	/// 解析某个 release 对应的对象存储 key(必要时触发镜像入桶),
	/// 供“获取临时链接”端点据此签发预签名 URL。不计入下载统计。
	/// </summary>
	Task<ObjectKeyResolution> ResolveObjectKeyAsync(
		string author, string name, int releaseId,
		string? userAgent, string? sourceId = null,
		CancellationToken ct = default);

	/// <summary>
	/// 计入一次下载统计(供 Web 302 直链路径调用 —— 该路径不经过代理流,需单独计数)。
	/// 本地发布更新本地统计;上游发布为无操作(交由上游统计)。
	/// </summary>
	Task CountDownloadAsync(
		string author, string name, int releaseId,
		string clientIp, string? userAgent, string? reason,
		CancellationToken ct = default);

	/// <summary>
	/// 取某 release 的建议下载文件名(“包名_版本.zip”)。本地/上游均支持;失败返回 null。
	/// </summary>
	Task<string?> GetSuggestedFileNameAsync(
		string author, string name, int releaseId, string? sourceId = null,
		CancellationToken ct = default);
}
