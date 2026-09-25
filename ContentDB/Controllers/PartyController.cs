// ContentDB C# —— 组队房间(网页端)

using ContentDB.Api.Auth;
using ContentDB.Core.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace ContentDB.Api.Controllers;

[ApiController]
public sealed class PartyController : ApiControllerBase
{
	private readonly IPartyService _party;

	public PartyController(ICurrentUserAccessor currentUser, IPartyService party)
		: base(currentUser) => _party = party;

	/// <summary>我所在房间的状态;不在房间返回 { party: null }。</summary>
	[HttpGet("/api/party/")]
	public async Task<IActionResult> State()
	{
		var (user, err) = await RequireUserAsync();
		if (err is not null) return err;
		return Ok(new { party = await _party.GetStateAsync(user!, HttpContext.RequestAborted) });
	}

	public sealed record CreatePartyBody(string? ServerAddress);

	[HttpPost("/api/party/")]
	public async Task<IActionResult> Create([FromBody] CreatePartyBody body)
	{
		var (user, err) = await RequireUserAsync();
		if (err is not null) return err;
		return FromResult(await _party.CreateAsync(user!, body.ServerAddress, HttpContext.RequestAborted));
	}

	public sealed record JoinPartyBody(string Code);

	[HttpPost("/api/party/join/")]
	public async Task<IActionResult> Join([FromBody] JoinPartyBody body)
	{
		var (user, err) = await RequireUserAsync();
		if (err is not null) return err;
		return FromResult(await _party.JoinAsync(user!, body.Code, HttpContext.RequestAborted));
	}

	[HttpPost("/api/party/leave/")]
	public async Task<IActionResult> Leave()
	{
		var (user, err) = await RequireUserAsync();
		if (err is not null) return err;
		return FromResult(await _party.LeaveAsync(user!, HttpContext.RequestAborted));
	}

	[HttpPost("/api/party/end/")]
	public async Task<IActionResult> End()
	{
		var (user, err) = await RequireUserAsync();
		if (err is not null) return err;
		return FromResult(await _party.EndAsync(user!, HttpContext.RequestAborted));
	}

	public sealed record SetServerBody(string Address);

	[HttpPost("/api/party/server/")]
	public async Task<IActionResult> SetServer([FromBody] SetServerBody body)
	{
		var (user, err) = await RequireUserAsync();
		if (err is not null) return err;
		return FromResult(await _party.SetServerAsync(user!, body.Address, HttpContext.RequestAborted));
	}

	public sealed record KickBody(string Username);

	[HttpPost("/api/party/kick/")]
	public async Task<IActionResult> Kick([FromBody] KickBody body)
	{
		var (user, err) = await RequireUserAsync();
		if (err is not null) return err;
		return FromResult(await _party.KickAsync(user!, body.Username, HttpContext.RequestAborted));
	}
}
