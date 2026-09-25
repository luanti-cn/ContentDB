// ContentDB C# —— API Token 管理端点(需登录会话或已有 token)

using ContentDB.Api.Auth;
using ContentDB.Core.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace ContentDB.Api.Controllers;

[ApiController]
public sealed class TokenController : ApiControllerBase
{
	private readonly ITokenService _tokens;

	public TokenController(ICurrentUserAccessor currentUser, ITokenService tokens)
		: base(currentUser) => _tokens = tokens;

	[HttpGet("/api/tokens/")]
	public async Task<IActionResult> List()
	{
		var (user, err) = await RequireUserAsync();
		if (err is not null) return err;
		return Ok(await _tokens.ListAsync(user!, HttpContext.RequestAborted));
	}

	public sealed record CreateTokenBody(string Name, string? PackageAuthor, string? PackageName);

	[HttpPost("/api/tokens/")]
	public async Task<IActionResult> Create([FromBody] CreateTokenBody body)
	{
		var (user, err) = await RequireUserAsync();
		if (err is not null) return err;
		return FromResult(await _tokens.CreateAsync(
			user!, body.Name, body.PackageAuthor, body.PackageName, HttpContext.RequestAborted));
	}

	[HttpDelete("/api/tokens/{id:int}/")]
	public async Task<IActionResult> Delete(int id)
	{
		var (user, err) = await RequireUserAsync();
		if (err is not null) return err;
		return FromResult(await _tokens.DeleteAsync(user!, id, HttpContext.RequestAborted));
	}

	/// <summary>当前身份自省(等价原 /api/whoami/)。</summary>
	[HttpGet("/api/whoami/")]
	public async Task<IActionResult> WhoAmI()
	{
		var user = await GetUserAsync();
		if (user is null) return Ok(new { is_authenticated = false, username = (string?)null });
		return Ok(new { is_authenticated = true, username = user.Username, rank = user.Rank.ToString() });
	}
}
