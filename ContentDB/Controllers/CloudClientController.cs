// ContentDB C# —— 云同步(客户端):配对 + 按服务器取用/回写凭证
// 使用配对换来的 device token(Bearer)认证,与网页会话/API Token 隔离。
//
// 客户端进服流程(参考):
//   1. GET  /api/cloud/client/vault/?address=host:port
//      - 200 => 拿 username/password 直接连服
//      - 404 => 用 default_username + GET /new-password/ 生成的随机密码在服务器注册
//      - 注册时若用户名被占用 => 提示用户改名后,用新名字继续注册
//   2. 注册/改名成功后 PUT /api/cloud/client/vault/ 回写云端保存

using ContentDB.Api.Auth;
using ContentDB.Core.Abstractions;
using ContentDB.Core.Domain;
using ContentDB.Core.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace ContentDB.Api.Controllers;

[ApiController]
public sealed class CloudClientController : ApiControllerBase
{
	private readonly IDevicePairingService _pairings;
	private readonly ICloudVaultService _vault;
	private readonly IPlayerCharacterService _characters;
	private readonly IFriendService _friends;
	private readonly IGameServerService _servers;
	private readonly IPartyService _party;
	private readonly IHostRoomService _rooms;

	public CloudClientController(ICurrentUserAccessor currentUser, IDevicePairingService pairings,
		ICloudVaultService vault, IPlayerCharacterService characters, IFriendService friends,
		IGameServerService servers, IPartyService party, IHostRoomService rooms)
		: base(currentUser)
	{
		_pairings = pairings;
		_vault = vault;
		_characters = characters;
		_friends = friends;
		_servers = servers;
		_party = party;
		_rooms = rooms;
	}

	/// <summary>校验 Bearer device token;返回(用户, 设备)。</summary>
	private async Task<(User? user, PairedDevice? device, IActionResult? error)> RequireDeviceAsync()
	{
		var result = await HttpContext.AuthenticateAsync(AuthenticationSetup.DeviceTokenScheme);
		if (!result.Succeeded)
			return (null, null, ApiError(401, "Device token needed (pair first)"));

		var device = HttpContext.Items[DeviceTokenAuthenticationHandler.DeviceItemKey] as PairedDevice;
		if (device is null || !device.IsActive)
			return (null, null, ApiError(401, "Device not found"));
		if (device.User is not { IsActive: true } || device.User.IsBanned)
			return (null, null, ApiError(403, "Account unavailable"));
		return (device.User, device, null);
	}

	// ---- 配对(匿名,配对码即凭据) ----

	public sealed record ClaimBody(string Code, string? DeviceName);

	[HttpPost("/api/cloud/client/pair/")]
	public async Task<IActionResult> Claim([FromBody] ClaimBody body)
		=> FromResult(await _pairings.ClaimAsync(body.Code, body.DeviceName, HttpContext.RequestAborted));

	// ---- 设备会话 ----

	[HttpGet("/api/cloud/client/me/")]
	public async Task<IActionResult> Me()
	{
		var (user, device, err) = await RequireDeviceAsync();
		if (err is not null) return err;
		return Ok(new
		{
			device = new { id = device!.Id, name = device.Name, last_used_at = device.LastUsedAt },
			user = new
			{
				username = user!.Username,
				display_name = user.DisplayName,
				default_server_username = user.DefaultServerUsername ?? user.Username,
			},
		});
	}

	/// <summary>生成一个适合 Luanti 服务器的随机密码(客户端注册新账号时用)。</summary>
	[HttpGet("/api/cloud/client/new-password/")]
	public async Task<IActionResult> NewPassword([FromQuery] int? length)
	{
		var (_, _, err) = await RequireDeviceAsync();
		if (err is not null) return err;
		await Task.CompletedTask;
		return Ok(new { password = RandomPassword.Generate(length ?? 16) });
	}

	// ---- 角色 ----

	/// <summary>角色列表(不含密码,供客户端进服前选择)。</summary>
	[HttpGet("/api/cloud/client/characters/")]
	public async Task<IActionResult> ListCharacters()
	{
		var (user, _, err) = await RequireDeviceAsync();
		if (err is not null) return err;
		return Ok(new { characters = await _characters.ListForClientAsync(user!, HttpContext.RequestAborted) });
	}

	// ---- 保管库 ----

	[HttpGet("/api/cloud/client/vault/")]
	public async Task<IActionResult> GetForServer([FromQuery] string address)
	{
		var (user, _, err) = await RequireDeviceAsync();
		if (err is not null) return err;

		var secret = await _vault.FindForServerAsync(user!, address, HttpContext.RequestAborted);
		if (secret is not null) return Ok(secret);

		var normalized = ServerAddress.Normalize(address) ?? address.Trim().ToLowerInvariant();
		return StatusCode(404, new
		{
			success = false,
			error = "No credentials for this server",
			address = normalized,
			default_username = user!.DefaultServerUsername ?? user.Username,
		});
	}

	public sealed record UpsertBody(string Address, string Username, string Password);

	[HttpPut("/api/cloud/client/vault/")]
	public async Task<IActionResult> Upsert([FromBody] UpsertBody body)
	{
		var (user, _, err) = await RequireDeviceAsync();
		if (err is not null) return err;
		return FromResult(await _vault.UpsertForServerAsync(user!, body.Address, body.Username, body.Password, HttpContext.RequestAborted));
	}

	public sealed record InvalidReportBody(string Address);

	/// <summary>
	/// 用存储凭证登录失败时上报(如密码在别处被改):
	/// 云端标记该凭证为「待更新」,网页会显示提示;客户端应提示用户输入新密码后 PUT 回写。
	/// 游戏内「Change Password」改密成功则不需要走这里,直接 PUT 同步即可。
	/// </summary>
	[HttpPost("/api/cloud/client/vault/invalid/")]
	public async Task<IActionResult> ReportInvalid([FromBody] InvalidReportBody body)
	{
		var (user, _, err) = await RequireDeviceAsync();
		if (err is not null) return err;
		return FromResult(await _vault.MarkInvalidAsync(user!, body.Address, HttpContext.RequestAborted));
	}

	public sealed record ProvisionBody(string Address, int? CharacterId);

	/// <summary>
	/// 进服统一入口:已有该服务器凭证则原样返回(Created=false);
	/// 否则按角色密码策略(或默认用户名+随机密码)生成新凭证(Created=true)。
	/// </summary>
	[HttpPost("/api/cloud/client/vault/provision/")]
	public async Task<IActionResult> Provision([FromBody] ProvisionBody body)
	{
		var (user, _, err) = await RequireDeviceAsync();
		if (err is not null) return err;
		return FromResult(await _characters.ProvisionAsync(user!, body.Address, body.CharacterId, HttpContext.RequestAborted));
	}

	// ---- 好友 / 联机在线状态 ----

	/// <summary>好友列表(含在线状态与当前所在服务器,供游戏内好友面板)。</summary>
	[HttpGet("/api/cloud/client/friends/")]
	public async Task<IActionResult> Friends()
	{
		var (user, _, err) = await RequireDeviceAsync();
		if (err is not null) return err;
		return Ok(new { friends = await _friends.ListFriendsAsync(user!, HttpContext.RequestAborted) });
	}

	public sealed record PresenceBody(string? Address);

	/// <summary>心跳:进服/退服/定时上报,供好友查看在线状态;建议 60 秒一次。</summary>
	[HttpPost("/api/cloud/client/presence/")]
	public async Task<IActionResult> Presence([FromBody] PresenceBody body)
	{
		var (_, device, err) = await RequireDeviceAsync();
		if (err is not null) return err;
		await _pairings.TouchPresenceAsync(device!, body.Address, HttpContext.RequestAborted);
		return Ok(new { success = true });
	}

	// ---- 服务器大厅 ----

	/// <summary>收录的服务器列表(游戏内服务器浏览器用)。</summary>
	[HttpGet("/api/cloud/client/servers/")]
	public async Task<IActionResult> Servers()
	{
		var (_, _, err) = await RequireDeviceAsync();
		if (err is not null) return err;
		return Ok(new { servers = await _servers.ListAsync(HttpContext.RequestAborted) });
	}

	// ---- 组队房间 ----

	[HttpGet("/api/cloud/client/party/")]
	public async Task<IActionResult> PartyState()
	{
		var (user, _, err) = await RequireDeviceAsync();
		if (err is not null) return err;
		return Ok(new { party = await _party.GetStateAsync(user!, HttpContext.RequestAborted) });
	}

	public sealed record CreatePartyBody(string? ServerAddress);

	[HttpPost("/api/cloud/client/party/")]
	public async Task<IActionResult> CreateParty([FromBody] CreatePartyBody body)
	{
		var (user, _, err) = await RequireDeviceAsync();
		if (err is not null) return err;
		return FromResult(await _party.CreateAsync(user!, body.ServerAddress, HttpContext.RequestAborted));
	}

	public sealed record JoinPartyBody(string Code);

	[HttpPost("/api/cloud/client/party/join/")]
	public async Task<IActionResult> JoinParty([FromBody] JoinPartyBody body)
	{
		var (user, _, err) = await RequireDeviceAsync();
		if (err is not null) return err;
		return FromResult(await _party.JoinAsync(user!, body.Code, HttpContext.RequestAborted));
	}

	[HttpPost("/api/cloud/client/party/leave/")]
	public async Task<IActionResult> LeaveParty()
	{
		var (user, _, err) = await RequireDeviceAsync();
		if (err is not null) return err;
		return FromResult(await _party.LeaveAsync(user!, HttpContext.RequestAborted));
	}

	[HttpPost("/api/cloud/client/party/end/")]
	public async Task<IActionResult> EndParty()
	{
		var (user, _, err) = await RequireDeviceAsync();
		if (err is not null) return err;
		return FromResult(await _party.EndAsync(user!, HttpContext.RequestAborted));
	}

	public sealed record SetServerBody(string Address);

	[HttpPost("/api/cloud/client/party/server/")]
	public async Task<IActionResult> SetPartyServer([FromBody] SetServerBody body)
	{
		var (user, _, err) = await RequireDeviceAsync();
		if (err is not null) return err;
		return FromResult(await _party.SetServerAsync(user!, body.Address, HttpContext.RequestAborted));
	}

	public sealed record KickBody(string Username);

	[HttpPost("/api/cloud/client/party/kick/")]
	public async Task<IActionResult> KickMember([FromBody] KickBody body)
	{
		var (user, _, err) = await RequireDeviceAsync();
		if (err is not null) return err;
		return FromResult(await _party.KickAsync(user!, body.Username, HttpContext.RequestAborted));
	}

	// ---- 联机房间(P2P 打洞 + 中继,信令走 /ws/) ----

	public sealed record HostRegisterBody(string? Status, object? Candidates);

	/// <summary>开服登记:launcher 检测到本地游戏后调用 → { roomId, roomCode }。</summary>
	[HttpPost("/api/cloud/client/host/register/")]
	public async Task<IActionResult> HostRegister([FromBody] HostRegisterBody body)
	{
		var (user, _, err) = await RequireDeviceAsync();
		if (err is not null) return err;
		return FromResult(await _rooms.RegisterAsync(user!, body?.Status,
			body?.Candidates is null ? null : JsonSerializer.Serialize(body.Candidates), HttpContext.RequestAborted));
	}

	[HttpPost("/api/cloud/client/host/heartbeat/")]
	public async Task<IActionResult> HostHeartbeat([FromBody] HostRegisterBody body)
	{
		var (user, _, err) = await RequireDeviceAsync();
		if (err is not null) return err;
		return FromResult(await _rooms.HeartbeatAsync(user!, body?.Status,
			body?.Candidates is null ? null : JsonSerializer.Serialize(body.Candidates), HttpContext.RequestAborted));
	}

	[HttpPost("/api/cloud/client/host/close/")]
	public async Task<IActionResult> HostClose()
	{
		var (user, _, err) = await RequireDeviceAsync();
		if (err is not null) return err;
		return FromResult(await _rooms.CloseAsync(user!, HttpContext.RequestAborted));
	}

	public sealed record HostJoinBody(string? RoomCode, string? Username);

	/// <summary>加入房间:roomCode(房间码)或 username(好友名)二选一;返回 Host 候选。</summary>
	[HttpPost("/api/cloud/client/host/join/")]
	public async Task<IActionResult> HostJoin([FromBody] HostJoinBody body)
	{
		var (user, _, err) = await RequireDeviceAsync();
		if (err is not null) return err;

		var result = body?.RoomCode is { Length: > 0 } code
			? await _rooms.JoinByCodeAsync(user!, code, HttpContext.RequestAborted)
			: await _rooms.JoinByUserAsync(user!, body?.Username ?? "", HttpContext.RequestAborted);
		return FromResult(result);
	}
}
