// ContentDB C# —— 好友私聊 REST(历史/发送/未读/已读)
// 双路由:网页 /api/messages/*(Cookie/API Token);客户端 /api/cloud/client/messages/*(device token)。
// 实时推送走 WS(dm.new / dm.read);REST 为历史、离线与兜底通道。

using ContentDB.Api.Auth;
using ContentDB.Core.Abstractions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;

namespace ContentDB.Api.Controllers;

[ApiController]
public sealed class MessagesController : ApiControllerBase
{
	private readonly IDirectMessageService _dm;

	public MessagesController(ICurrentUserAccessor currentUser, IDirectMessageService dm)
		: base(currentUser) => _dm = dm;

	// ---- 网页路由(Cookie / API Token) ----

	[HttpGet("/api/messages/{username}/")]
	public Task<IActionResult> HistoryWeb(string username, [FromQuery] long? before, [FromQuery] int limit = 50)
		=> HistoryImpl(username, before, limit);

	[HttpPost("/api/messages/{username}/")]
	public Task<IActionResult> SendWeb(string username, [FromBody] SendBody body)
		=> SendImpl(username, body);

	[HttpGet("/api/messages/unread/")]
	public Task<IActionResult> UnreadWeb() => UnreadImpl();

	[HttpPost("/api/messages/{username}/read/")]
	public Task<IActionResult> ReadWeb(string username) => ReadImpl(username);

	// ---- 客户端路由(device token) ----

	[HttpGet("/api/cloud/client/messages/{username}/")]
	public Task<IActionResult> HistoryClient(string username, [FromQuery] long? before, [FromQuery] int limit = 50)
		=> HistoryImpl(username, before, limit);

	[HttpPost("/api/cloud/client/messages/{username}/")]
	public Task<IActionResult> SendClient(string username, [FromBody] SendBody body)
		=> SendImpl(username, body);

	[HttpGet("/api/cloud/client/messages/unread/")]
	public Task<IActionResult> UnreadClient() => UnreadImpl();

	[HttpPost("/api/cloud/client/messages/{username}/read/")]
	public Task<IActionResult> ReadClient(string username) => ReadImpl(username);

	// ---- 实现 ----

	public sealed record SendBody(string Body);

	private async Task<IActionResult> HistoryImpl(string username, long? before, int limit)
	{
		var (user, err) = await RequireUserOrDeviceAsync();
		if (err is not null) return err;
		var page = await _dm.HistoryAsync(user!, username, before, limit, HttpContext.RequestAborted);
		return Ok(new { messages = page.Items, hasMore = page.HasMore });
	}

	private async Task<IActionResult> SendImpl(string username, SendBody body)
	{
		var (user, err) = await RequireUserOrDeviceAsync();
		if (err is not null) return err;
		return FromResult(await _dm.SendAsync(user!, username, body?.Body ?? "", HttpContext.RequestAborted));
	}

	private async Task<IActionResult> UnreadImpl()
	{
		var (user, err) = await RequireUserOrDeviceAsync();
		if (err is not null) return err;
		var unread = await _dm.UnreadAsync(user!, HttpContext.RequestAborted);
		return Ok(new { unread, total = unread.Sum(u => u.Count) });
	}

	private async Task<IActionResult> ReadImpl(string username)
	{
		var (user, err) = await RequireUserOrDeviceAsync();
		if (err is not null) return err;
		var count = await _dm.MarkReadAsync(user!, username, HttpContext.RequestAborted);
		return Ok(new { success = true, marked = count });
	}

	/// <summary>会话用户或 device token 用户任一即可(客户端路由给 launcher 用)。</summary>
	private async Task<(Core.Domain.User? user, IActionResult? error)> RequireUserOrDeviceAsync()
	{
		var (user, err) = await RequireUserAsync();
		if (err is null) return (user, null);

		var dev = await HttpContext.AuthenticateAsync(AuthenticationSetup.DeviceTokenScheme);
		if (dev.Succeeded
			&& HttpContext.Items[DeviceTokenAuthenticationHandler.DeviceItemKey] is Core.Domain.PairedDevice device
			&& device.IsActive && device.User is { IsActive: true } && !device.User.IsBanned)
			return (device.User, null);

		return (null, err);
	}
}
