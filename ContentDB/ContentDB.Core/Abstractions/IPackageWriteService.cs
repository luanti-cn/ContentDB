// ContentDB C# —— 包写服务抽象(创建/编辑/删除 + 审核状态机)

using ContentDB.Core.Domain;

namespace ContentDB.Core.Abstractions;

public sealed record PackageEditInput(
	string? Title,
	string? ShortDescription,
	string? LongDescription,
	string? Type,
	string? License,
	string? MediaLicense,
	string? Repo,
	string? Website,
	string? IssueTracker,
	int? Forums,
	string? VideoUrl,
	string? DonateUrl,
	string? TranslationUrl,
	string? DevState,
	List<string>? Tags,
	List<string>? ContentWarnings);

public sealed record ServiceResult(bool Success, int StatusCode, string? Error, object? Payload)
{
	public static ServiceResult Ok(object? payload = null) => new(true, 200, null, payload);
	public static ServiceResult Fail(int status, string error) => new(false, status, error, null);
}

public interface IPackageWriteService
{
	Task<Package?> FindAsync(string author, string name, CancellationToken ct = default);

	Task<ServiceResult> CreateAsync(User actor, string name, PackageEditInput input, CancellationToken ct = default);

	Task<ServiceResult> EditAsync(User actor, Package package, PackageEditInput input, CancellationToken ct = default);

	Task<ServiceResult> DeleteAsync(User actor, Package package, CancellationToken ct = default);

	/// <summary>审核状态迁移(WIP -> READY_FOR_REVIEW -> APPROVED / CHANGES_NEEDED / DELETED)。</summary>
	Task<ServiceResult> MoveToStateAsync(User actor, Package package, PackageState target, CancellationToken ct = default);
}
