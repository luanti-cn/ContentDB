// ContentDB C# —— 好友(网页端):申请/接受/拒绝/删除/拉黑

using ContentDB.Api.Auth;
using ContentDB.Core.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace ContentDB.Api.Controllers;

[ApiController]
public sealed class FriendsController : ApiControllerBase
{
	private readonly IFriendService _friends;

	public FriendsController(ICurrentUserAccessor currentUser, IFriendService friends)
		: base(currentUser) => _friends = friends;

	[HttpGet("/api/friends/")]
	public async Task<IActionResult> List()
	{
		var (user, err) = await RequireUserAsync();
		if (err is not null) return err;
		return Ok(new { friends = await _friends.ListFriendsAsync(user!, HttpContext.RequestAborted) });
	}

	[HttpGet("/api/friends/requests/")]
	public async Task<IActionResult> Requests()
	{
		var (user, err) = await RequireUserAsync();
		if (err is not null) return err;
		var (incoming, outgoing) = await _friends.ListRequestsAsync(user!, HttpContext.RequestAborted);
		return Ok(new { incoming, outgoing });
	}

	public sealed record SendRequestBody(string Username);

	[HttpPost("/api/friends/requests/")]
	public async Task<IActionResult> Send([FromBody] SendRequestBody body)
	{
		var (user, err) = await RequireUserAsync();
		if (err is not null) return err;
		return FromResult(await _friends.SendRequestAsync(user!, body.Username, HttpContext.RequestAborted));
	}

	[HttpPost("/api/friends/requests/{id:int}/accept/")]
	public async Task<IActionResult> Accept(int id)
	{
		var (user, err) = await RequireUserAsync();
		if (err is not null) return err;
		return FromResult(await _friends.AcceptAsync(user!, id, HttpContext.RequestAborted));
	}

	[HttpDelete("/api/friends/requests/{id:int}/")]
	public async Task<IActionResult> RejectOrWithdraw(int id)
	{
		var (user, err) = await RequireUserAsync();
		if (err is not null) return err;
		return FromResult(await _friends.RejectOrWithdrawAsync(user!, id, HttpContext.RequestAborted));
	}

	[HttpDelete("/api/friends/{username}/")]
	public async Task<IActionResult> Remove(string username)
	{
		var (user, err) = await RequireUserAsync();
		if (err is not null) return err;
		return FromResult(await _friends.RemoveFriendAsync(user!, username, HttpContext.RequestAborted));
	}

	[HttpPost("/api/friends/{username}/block/")]
	public async Task<IActionResult> Block(string username)
	{
		var (user, err) = await RequireUserAsync();
		if (err is not null) return err;
		return FromResult(await _friends.BlockAsync(user!, username, HttpContext.RequestAborted));
	}

	[HttpDelete("/api/friends/{username}/block/")]
	public async Task<IActionResult> Unblock(string username)
	{
		var (user, err) = await RequireUserAsync();
		if (err is not null) return err;
		return FromResult(await _friends.UnblockAsync(user!, username, HttpContext.RequestAborted));
	}
}
