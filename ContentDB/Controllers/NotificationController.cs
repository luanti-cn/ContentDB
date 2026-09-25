// ContentDB C# —— 通知端点(需登录会话或 API Token)

using ContentDB.Api.Auth;
using ContentDB.Core.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace ContentDB.Api.Controllers;

[ApiController]
public sealed class NotificationController : ApiControllerBase
{
	private readonly INotificationService _notifications;

	public NotificationController(ICurrentUserAccessor currentUser, INotificationService notifications)
		: base(currentUser) => _notifications = notifications;

	[HttpGet("/api/notifications/")]
	public async Task<IActionResult> List()
	{
		var (user, err) = await RequireUserAsync();
		if (err is not null) return err;

		var unreadOnly = Request.Query["unread"].ToString() is "true" or "1";
		var limit = int.TryParse(Request.Query["n"], out var n) ? n : 100;
		return Ok(await _notifications.ListAsync(user!, unreadOnly, limit, HttpContext.RequestAborted));
	}

	[HttpPost("/api/notifications/{id:int}/read/")]
	public async Task<IActionResult> MarkRead(int id)
	{
		var (user, err) = await RequireUserAsync();
		if (err is not null) return err;
		return FromResult(await _notifications.MarkReadAsync(user!, id, HttpContext.RequestAborted));
	}

	[HttpPost("/api/notifications/read-all/")]
	public async Task<IActionResult> MarkAllRead()
	{
		var (user, err) = await RequireUserAsync();
		if (err is not null) return err;
		return FromResult(await _notifications.MarkReadAsync(user!, null, HttpContext.RequestAborted));
	}
}
