// ContentDB C# —— 合并读取服务抽象
// 把"本站自主运营(LOCAL)"与"上游官方(UPSTREAM,经代理/缓存)"内容在读取时合并。

namespace ContentDB.Core.Abstractions;

public sealed record MergedResult(int StatusCode, string Body, string? ContentType);

public interface IMergedReadService
{
	/// <summary>
	/// /api/packages/ 的合并实现:本站 + 多个上游站点(去重,本站优先),各带 origin/source 标记。
	/// 排序:本站按时间倒序在前,各外站按 Order、站内按时间倒序在后。
	/// </summary>
	/// <param name="isLuantiClient">是否 Luanti 客户端;为 true 时在简介前加站点标签 [Luanti.org]/[Luanti.cn]。</param>
	Task<MergedResult> GetPackagesAsync(
		IReadOnlyDictionary<string, List<string>> args,
		string? acceptLanguage,
		string? userAgent,
		string? fmt,
		bool isLuantiClient,
		CancellationToken ct = default);

	/// <summary>
	/// /api/updates/ 的合并实现:本地 + 上游的 {author/name: release_id}。
	/// </summary>
	Task<MergedResult> GetUpdatesAsync(
		IReadOnlyDictionary<string, List<string>> args,
		string? userAgent,
		CancellationToken ct = default);

	/// <summary>
	/// 本站单包详情 /api/packages/{author}/{name}/。
	/// 命中本站返回 as_dict JSON;本站无此包返回 null(由调用方回退上游)。
	/// </summary>
	Task<MergedResult?> GetLocalPackageAsync(string author, string name, CancellationToken ct = default);

	/// <summary>
	/// 本站单包发布列表 /api/packages/{author}/{name}/releases/。
	/// 命中本站包返回其发布数组(可能为空数组);本站无此包返回 null(回退上游)。
	/// </summary>
	Task<MergedResult?> GetLocalReleasesAsync(string author, string name, CancellationToken ct = default);

	/// <summary>
	/// 本站单包截图列表 /api/packages/{author}/{name}/screenshots/。
	/// 命中本站包返回其截图数组(可能为空数组);本站无此包返回 null(回退上游)。
	/// </summary>
	Task<MergedResult?> GetLocalScreenshotsAsync(string author, string name, CancellationToken ct = default);
}
