// ContentDB C# —— 通知 + 审计日志服务实现

using ContentDB.Core.Abstractions;
using ContentDB.Core.Domain;
using ContentDB.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContentDB.Infrastructure.Services;

public sealed class NotificationService : INotificationService
{
	private readonly AppDbContext _db;

	public NotificationService(AppDbContext db) => _db = db;

	public async Task NotifyAsync(int userId, int? causerId, NotificationType type, string title, string url,
		int? packageId = null, CancellationToken ct = default)
	{
		// 不给自己发通知
		if (causerId is not null && causerId == userId) return;

		_db.Notifications.Add(new Notification
		{
			UserId = userId,
			CauserId = causerId,
			Type = type,
			Title = title,
			Url = url,
			PackageId = packageId,
			Read = false,
			Emailed = false,
			CreatedAt = DateTimeOffset.UtcNow,
		});
		await _db.SaveChangesAsync(ct);
	}

	public async Task<IReadOnlyList<NotificationDto>> ListAsync(User user, bool unreadOnly, int limit, CancellationToken ct = default)
	{
		var q = _db.Notifications
			.Where(n => n.UserId == user.Id);
		if (unreadOnly) q = q.Where(n => !n.Read);

		return await q
			.OrderByDescending(n => n.CreatedAt)
			.Take(Math.Clamp(limit, 1, 200))
			.Select(n => new NotificationDto(
				n.Id, n.Title, n.Url, n.Type.ToString(), n.Read, n.CreatedAt,
				n.Causer != null ? n.Causer.Username : null,
				n.Package != null ? n.Package.Author.Username + "/" + n.Package.Name : null))
			.ToListAsync(ct);
	}

	public async Task<ServiceResult> MarkReadAsync(User user, int? notificationId, CancellationToken ct = default)
	{
		if (notificationId is int id)
		{
			var n = await _db.Notifications.FirstOrDefaultAsync(x => x.Id == id && x.UserId == user.Id, ct);
			if (n is null) return ServiceResult.Fail(404, "Notification not found");
			n.Read = true;
		}
		else
		{
			await _db.Notifications.Where(x => x.UserId == user.Id && !x.Read)
				.ExecuteUpdateAsync(s => s.SetProperty(x => x.Read, true), ct);
			return ServiceResult.Ok(new { success = true });
		}
		await _db.SaveChangesAsync(ct);
		return ServiceResult.Ok(new { success = true });
	}

	public async Task AuditAsync(int? causerId, AuditSeverity severity, string title, int? packageId = null,
		string? url = null, string? description = null, CancellationToken ct = default)
	{
		_db.AuditLog.Add(new AuditLogEntry
		{
			CauserId = causerId,
			Severity = severity,
			Title = title,
			PackageId = packageId,
			Url = url,
			Description = description,
			CreatedAt = DateTimeOffset.UtcNow,
		});
		await _db.SaveChangesAsync(ct);
	}
}
