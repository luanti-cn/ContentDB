// ContentDB C# —— 用户等级与权限
// 从原 Python users.py 的 UserRank / Permission 忠实移植。

namespace ContentDB.Core.Domain;

/// <summary>
/// 用户等级(有序)。数值越高权限越大,用 AtLeast 比较。
/// </summary>
public enum UserRank
{
	BANNED = 0,
	NOT_JOINED,
	NEW_MEMBER,
	MEMBER,
	TRUSTED_MEMBER,
	APPROVER,
	EDITOR,
	BOT,
	MODERATOR,
	ADMIN,
}

public static class UserRankExtensions
{
	/// <summary>当前等级是否至少达到 <paramref name="min"/>。BOT 特殊处理与原站一致(BOT 仅等于自身用途)。</summary>
	public static bool AtLeast(this UserRank rank, UserRank min) => (int)rank >= (int)min;

	public static string ToName(this UserRank rank) => rank.ToString().ToLowerInvariant();
}

/// <summary>
/// 权限词汇表。上下文相关,由各实体的 CheckPerm 解释。
/// </summary>
public enum Permission
{
	EDIT_PACKAGE,
	APPROVE_CHANGES,
	DELETE_PACKAGE,
	CHANGE_AUTHOR,
	CHANGE_NAME,
	MAKE_RELEASE,
	DELETE_RELEASE,
	ADD_SCREENSHOTS,
	REORDER_SCREENSHOTS,
	APPROVE_SCREENSHOT,
	APPROVE_RELEASE,
	APPROVE_NEW,
	EDIT_MAINTAINERS,
	CHANGE_RELEASE_URL,
	UNAPPROVE_PACKAGE,
	TOPIC_DISCARD,
	CREATE_THREAD,
	COMMENT_THREAD,
	LOCK_THREAD,
	DELETE_THREAD,
	DELETE_REPLY,
	EDIT_REPLY,
	SEE_THREAD,
	CREATE_TOKEN,
	CHANGE_USERNAMES,
	CHANGE_RANK,
	CHANGE_EMAIL,
	CHANGE_PROFILE_URLS,
	CHANGE_DISPLAY_NAME,
	SEE_PACKAGE_AUDIT_LOG,
	CREATE_OAUTH_CLIENT,
	APPROVE_OAUTH_CLIENT,
	MULTI_APPROVERS,
	REVIEW_PACKAGES,
	ADD_REVIEW,
	VIEW_COLLECTION,
	EDIT_COLLECTION,
	VIEW_PACKAGE,
}
