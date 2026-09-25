// ContentDB C# Mirror
// 文件镜像服务抽象:把某上游站点的 /uploads/<file> 懒拉取到对象存储,并给出下载重定向目标。

using ContentDB.Core.Configuration;

namespace ContentDB.Core.Abstractions;

public sealed record FileResolveResult(
	bool Success,
	string? RedirectUrl,   // 若有公共 CDN URL,则为 302 目标
	Stream? Content,       // 否则由镜像自身代理文件流
	string? ContentType,
	long? ContentLength,
	int UpstreamStatus);

public interface IFileMirrorService
{
	/// <summary>
	/// 确保某上游站点的文件已镜像入对象存储,并返回如何交付给客户端。
	/// 首次未命中时:同步从该站拉取写入对象存储,然后返回公共 URL(或流)。
	/// </summary>
	/// <param name="source">上游站点(提供 baseUrl / id 用于回源与对象 key 分区)</param>
	/// <param name="upstreamPath">该站文件路径,如 "/uploads/abc.zip" 或 download 端点路径</param>
	/// <param name="userAgent">客户端 UA(回源透传)</param>
	Task<FileResolveResult> EnsureAndResolveAsync(
		UpstreamSiteOptions source,
		string upstreamPath,
		string? userAgent = null,
		CancellationToken ct = default);

	/// <summary>
	/// 确保某上游文件已同步写入对象存储(**同步等待**入桶完成),并返回其对象 key。
	/// 供“获取临时链接”端点使用——签发预签名 URL 前对象必须真实存在于桶中。
	/// 若上游拉取或校验失败,返回 null。
	/// </summary>
	Task<string?> EnsureStoredAndGetKeyAsync(
		UpstreamSiteOptions source,
		string upstreamPath,
		string? userAgent = null,
		CancellationToken ct = default);
}
