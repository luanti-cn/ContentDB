// ContentDB C# —— 发布/截图/线程/评价/合集 写服务抽象

using ContentDB.Core.Domain;

namespace ContentDB.Core.Abstractions;

public interface IReleaseWriteService
{
	/// <summary>zip 上传创建发布(校验 + 存对象存储)。</summary>
	Task<ServiceResult> CreateZipReleaseAsync(
		User actor, Package package, string name, string title,
		string? releaseNotes, Stream zipStream, long zipLength, string? commitHash,
		CancellationToken ct = default);

	/// <summary>git 引用创建发布(异步任务打包,先建 PROCESSING 记录)。</summary>
	Task<ServiceResult> CreateVcsReleaseAsync(
		User actor, Package package, string name, string title,
		string? releaseNotes, string gitRef, CancellationToken ct = default);

	Task<ServiceResult> DeleteReleaseAsync(User actor, Package package, int releaseId, CancellationToken ct = default);

	Task<ServiceResult> ApproveReleaseAsync(User actor, Package package, int releaseId, CancellationToken ct = default);
}

public interface IScreenshotWriteService
{
	Task<ServiceResult> CreateAsync(User actor, Package package, string title, Stream image, long length,
		string fileName, bool isCoverImage, CancellationToken ct = default);

	Task<ServiceResult> DeleteAsync(User actor, Package package, int screenshotId, CancellationToken ct = default);

	Task<ServiceResult> ReorderAsync(User actor, Package package, IReadOnlyList<int> orderedIds, CancellationToken ct = default);

	Task<ServiceResult> SetCoverImageAsync(User actor, Package package, int screenshotId, CancellationToken ct = default);
}

public interface IThreadWriteService
{
	Task<ServiceResult> CreateThreadAsync(User actor, string? packageAuthor, string? packageName,
		string title, string firstComment, bool isPrivate, CancellationToken ct = default);

	Task<ServiceResult> ReplyAsync(User actor, int threadId, string comment, CancellationToken ct = default);

	Task<ServiceResult> SetLockedAsync(User actor, int threadId, bool locked, CancellationToken ct = default);
}

public interface IReviewWriteService
{
	Task<ServiceResult> CreateOrUpdateAsync(User actor, Package package, int rating,
		string title, string comment, string? language, CancellationToken ct = default);

	Task<ServiceResult> DeleteAsync(User actor, Package package, CancellationToken ct = default);

	Task<ServiceResult> VoteAsync(User actor, int reviewId, bool isPositive, CancellationToken ct = default);
}

public interface ICollectionWriteService
{
	Task<ServiceResult> CreateAsync(User actor, string name, string title, string shortDesc,
		string? longDesc, bool isPrivate, CancellationToken ct = default);

	Task<ServiceResult> EditAsync(User actor, string author, string name, string? title,
		string? shortDesc, string? longDesc, bool? isPrivate, CancellationToken ct = default);

	Task<ServiceResult> DeleteAsync(User actor, string author, string name, CancellationToken ct = default);

	Task<ServiceResult> AddPackageAsync(User actor, string author, string name,
		string pkgAuthor, string pkgName, string? description, CancellationToken ct = default);

	Task<ServiceResult> RemovePackageAsync(User actor, string author, string name,
		string pkgAuthor, string pkgName, CancellationToken ct = default);
}
