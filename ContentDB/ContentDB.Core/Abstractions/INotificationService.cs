// ContentDB C# —— 通知 + 审计日志服务抽象

using ContentDB.Core.Domain;

namespace ContentDB.Core.Abstractions;

public sealed record NotificationDto(
	int Id, string Title, string Url, string Type,
	bool Read, DateTimeOffset CreatedAt, string? CauserUsername, string? PackageKey);

public interface INotificationService
{
	/// <summary>给某用户创建一条通知(causer 可空;同一 causer 给自己不发)。</summary>
	Task NotifyAsync(int userId, int? causerId, NotificationType type, string title, string url,
		int? packageId = null, CancellationToken ct = default);

	/// <summary>列出用户通知(最新在前)。</summary>
	Task<IReadOnlyList<NotificationDto>> ListAsync(User user, bool unreadOnly, int limit, CancellationToken ct = default);

	/// <summary>标记单条/全部已读。</summary>
	Task<ServiceResult> MarkReadAsync(User user, int? notificationId, CancellationToken ct = default);

	/// <summary>写审计日志。</summary>
	Task AuditAsync(int? causerId, AuditSeverity severity, string title, int? packageId = null,
		string? url = null, string? description = null, CancellationToken ct = default);
}
