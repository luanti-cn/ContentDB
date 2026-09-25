// ContentDB C# —— API Token / OAuth 客户端 / 通知 / 审计日志 / 论坛主题

namespace ContentDB.Core.Domain;

public class APIToken
{
	public int Id { get; set; }

	public string Name { get; set; } = "";
	public string AccessToken { get; set; } = "";

	public int OwnerId { get; set; }
	public User Owner { get; set; } = null!;

	/// <summary>可选:限定该 token 只能操作某个包。</summary>
	public int? PackageId { get; set; }
	public Package? Package { get; set; }

	/// <summary>可选:由某 OAuth 客户端代表用户签发。</summary>
	public string? ClientId { get; set; }
	public OAuthClient? Client { get; set; }

	public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

	public bool CanOperateOnPackage(Package package)
		=> PackageId is null || PackageId == package.Id;
}

public class OAuthClient
{
	/// <summary>字符串主键(随机 id)。</summary>
	public string Id { get; set; } = "";

	public string Title { get; set; } = "";
	public string Secret { get; set; } = "";
	public string RedirectUrl { get; set; } = "";
	public string? Description { get; set; }

	public bool Approved { get; set; }
	public bool Verified { get; set; }
	public bool IsClientSide { get; set; }

	public int OwnerId { get; set; }
	public User Owner { get; set; } = null!;

	public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public enum NotificationType
{
	PACKAGE_EDIT,
	PACKAGE_APPROVAL,
	NEW_THREAD,
	NEW_REVIEW,
	THREAD_REPLY,
	BOT,
	MAINTAINER,
	EDITOR_ALERT,
	EDITOR_MISC,
	FRIEND_REQUEST,
	FRIEND_ONLINE,
	OTHER,
}

public class Notification
{
	public int Id { get; set; }

	public int UserId { get; set; }
	public User User { get; set; } = null!;

	public int? CauserId { get; set; }
	public User? Causer { get; set; }

	public NotificationType Type { get; set; } = NotificationType.OTHER;
	public string Title { get; set; } = "";
	public string Url { get; set; } = "";

	public int? PackageId { get; set; }
	public Package? Package { get; set; }

	public bool Read { get; set; }
	public bool Emailed { get; set; }
	public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public enum AuditSeverity
{
	NORMAL,
	EDITOR,
	MODERATION,
}

public class AuditLogEntry
{
	public int Id { get; set; }

	public int? CauserId { get; set; }
	public User? Causer { get; set; }

	public AuditSeverity Severity { get; set; } = AuditSeverity.NORMAL;
	public string Title { get; set; } = "";
	public string? Url { get; set; }

	public int? PackageId { get; set; }
	public Package? Package { get; set; }

	public string? Description { get; set; }
	public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class ForumTopic
{
	public int TopicId { get; set; }  // 论坛主题 id 作为主键

	public int AuthorId { get; set; }
	public User? Author { get; set; }

	public string Title { get; set; } = "";
	public string Name { get; set; } = "";
	public PackageType Type { get; set; }
	public int Views { get; set; }
	public bool Wip { get; set; }
	public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
