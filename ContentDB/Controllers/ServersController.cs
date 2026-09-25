// ContentDB C# —— 服务器大厅(网页端管理 + 公开列表 + 服务器端 mod 上报)

using ContentDB.Api.Auth;
using ContentDB.Core.Abstractions;
using ContentDB.Core.Domain;
using Microsoft.AspNetCore.Mvc;

namespace ContentDB.Api.Controllers;

[ApiController]
public sealed class ServersController : ControllerBase
{
	private readonly IGameServerService _servers;
	private readonly ICurrentUserAccessor _currentUser;

	public ServersController(IGameServerService servers, ICurrentUserAccessor currentUser)
	{
		_servers = servers;
		_currentUser = currentUser;
	}

	private async Task<(User? user, IActionResult? error)> RequireUserAsync()
	{
		var user = await _currentUser.GetAsync(HttpContext.RequestAborted);
		if (user is null) return (null, StatusCode(401, new { success = false, error = "Authentication needed" }));
		if (user.IsBanned) return (null, StatusCode(403, new { success = false, error = "Account is banned" }));
		return (user, null);
	}

	private IActionResult FromResult(ServiceResult result)
	{
		if (result.Success)
			return StatusCode(result.StatusCode, result.Payload ?? new { success = true });
		return StatusCode(result.StatusCode, new { success = false, error = result.Error });
	}

	/// <summary>公开服务器列表(无需登录)。</summary>
	[HttpGet("/api/servers/")]
	public async Task<IActionResult> List()
		=> Ok(new { servers = await _servers.ListAsync(HttpContext.RequestAborted) });

	public sealed record RegisterBody(string Address, string Name, string? Description, string? WebsiteUrl);

	[HttpPost("/api/servers/")]
	public async Task<IActionResult> Register([FromBody] RegisterBody body)
	{
		var (user, err) = await RequireUserAsync();
		if (err is not null) return err;
		return FromResult(await _servers.RegisterAsync(
			user!, body.Address, body.Name, body.Description, body.WebsiteUrl, HttpContext.RequestAborted));
	}

	[HttpGet("/api/servers/mine/")]
	public async Task<IActionResult> Mine()
	{
		var (user, err) = await RequireUserAsync();
		if (err is not null) return err;
		return Ok(new { servers = await _servers.ListMineAsync(user!, HttpContext.RequestAborted) });
	}

	public sealed record UpdateServerBody(string? Name, string? Description, string? WebsiteUrl, bool? Listed);

	[HttpPut("/api/servers/{id:int}/")]
	public async Task<IActionResult> Update(int id, [FromBody] UpdateServerBody body)
	{
		var (user, err) = await RequireUserAsync();
		if (err is not null) return err;
		return FromResult(await _servers.UpdateAsync(
			user!, id, body.Name, body.Description, body.WebsiteUrl, body.Listed, HttpContext.RequestAborted));
	}

	[HttpDelete("/api/servers/{id:int}/")]
	public async Task<IActionResult> Delete(int id)
	{
		var (user, err) = await RequireUserAsync();
		if (err is not null) return err;
		return FromResult(await _servers.DeleteAsync(user!, id, HttpContext.RequestAborted));
	}

	/// <summary>重新生成上报 token(明文仅此次返回)。</summary>
	[HttpPost("/api/servers/{id:int}/token/")]
	public async Task<IActionResult> RegenerateToken(int id)
	{
		var (user, err) = await RequireUserAsync();
		if (err is not null) return err;
		return FromResult(await _servers.RegenerateReportTokenAsync(user!, id, HttpContext.RequestAborted));
	}

	public sealed record ReportBody(string Address, string Token, int PlayersOnline, int PlayersMax, string? Motd);

	/// <summary>服务器端 mod 定期上报(建议 60 秒一次);token 鉴权,不计下载统计。</summary>
	[HttpPost("/api/servers/report/")]
	public async Task<IActionResult> Report([FromBody] ReportBody body)
		=> FromResult(await _servers.ReportStatusAsync(
			body.Address, body.Token, body.PlayersOnline, body.PlayersMax, body.Motd, HttpContext.RequestAborted));

	// ---- 管理员 ----

	/// <summary>管理员:全量服务器(含未上架),供收录审核。</summary>
	[HttpGet("/api/servers/admin/")]
	public async Task<IActionResult> AdminList()
	{
		var (user, err) = await RequireUserAsync();
		if (err is not null) return err;
		if (!user!.Rank.AtLeast(UserRank.MODERATOR))
			return StatusCode(403, new { success = false, error = "Moderator privileges required" });
		return Ok(new { servers = await _servers.AdminListAsync(HttpContext.RequestAborted) });
	}

	public sealed record AdminReviewBody(bool? Verified, bool? Listed);

	/// <summary>管理员:设置认证标记 / 上架状态(收录审核)。</summary>
	[HttpPost("/api/servers/admin/{id:int}/review/")]
	public async Task<IActionResult> AdminReview(int id, [FromBody] AdminReviewBody body)
	{
		var (user, err) = await RequireUserAsync();
		if (err is not null) return err;
		if (!user!.Rank.AtLeast(UserRank.MODERATOR))
			return StatusCode(403, new { success = false, error = "Moderator privileges required" });
		return FromResult(await _servers.AdminSetFlagsAsync(
			user!, id, body.Verified, body.Listed, HttpContext.RequestAborted));
	}
}
