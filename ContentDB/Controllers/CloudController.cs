// ContentDB C# —— 云同步(网页端):设备管理 / 配对 / 默认用户名 / 服务器账号保管库
// 需登录会话或 API Token。

using ContentDB.Api.Auth;
using ContentDB.Core.Abstractions;
using ContentDB.Core.Domain;
using Microsoft.AspNetCore.Mvc;

namespace ContentDB.Api.Controllers;

[ApiController]
public sealed class CloudController : ApiControllerBase
{
	private readonly IDevicePairingService _pairings;
	private readonly ICloudVaultService _vault;
	private readonly IPlayerCharacterService _characters;

	public CloudController(ICurrentUserAccessor currentUser, IDevicePairingService pairings,
		ICloudVaultService vault, IPlayerCharacterService characters)
		: base(currentUser)
	{
		_pairings = pairings;
		_vault = vault;
		_characters = characters;
	}

	// ---- 设备 ----

	[HttpGet("/api/cloud/devices/")]
	public async Task<IActionResult> ListDevices()
	{
		var (user, err) = await RequireUserAsync();
		if (err is not null) return err;
		return Ok(new { devices = await _pairings.ListDevicesAsync(user!, HttpContext.RequestAborted) });
	}

	public sealed record CreatePairingBody(string DeviceName);

	[HttpPost("/api/cloud/devices/pairing/")]
	public async Task<IActionResult> CreatePairing([FromBody] CreatePairingBody body)
	{
		var (user, err) = await RequireUserAsync();
		if (err is not null) return err;
		return FromResult(await _pairings.CreatePairingAsync(user!, body.DeviceName, HttpContext.RequestAborted));
	}

	[HttpGet("/api/cloud/devices/pairing/{id}/")]
	public async Task<IActionResult> PairingStatus(string id)
	{
		var (user, err) = await RequireUserAsync();
		if (err is not null) return err;
		return FromResult(await _pairings.GetPairingStatusAsync(user!, id, HttpContext.RequestAborted));
	}

	[HttpDelete("/api/cloud/devices/{id:int}/")]
	public async Task<IActionResult> RevokeDevice(int id)
	{
		var (user, err) = await RequireUserAsync();
		if (err is not null) return err;
		return FromResult(await _pairings.RevokeDeviceAsync(user!, id, HttpContext.RequestAborted));
	}

	// ---- 设置 ----

	[HttpGet("/api/cloud/settings/")]
	public async Task<IActionResult> GetSettings()
	{
		var (user, err) = await RequireUserAsync();
		if (err is not null) return err;
		return Ok(new { default_server_username = user!.DefaultServerUsername ?? user.Username });
	}

	public sealed record UpdateSettingsBody(string? DefaultServerUsername);

	[HttpPut("/api/cloud/settings/")]
	public async Task<IActionResult> UpdateSettings([FromBody] UpdateSettingsBody body)
	{
		var (user, err) = await RequireUserAsync();
		if (err is not null) return err;
		return FromResult(await _vault.SetDefaultUsernameAsync(user!, body.DefaultServerUsername, HttpContext.RequestAborted));
	}

	// ---- 保管库 ----

	[HttpGet("/api/cloud/vault/")]
	public async Task<IActionResult> ListVault()
	{
		var (user, err) = await RequireUserAsync();
		if (err is not null) return err;
		return Ok(new { entries = await _vault.ListAsync(user!, HttpContext.RequestAborted) });
	}

	public sealed record AddVaultBody(string Address, string Username, string Password);

	[HttpPost("/api/cloud/vault/")]
	public async Task<IActionResult> AddVault([FromBody] AddVaultBody body)
	{
		var (user, err) = await RequireUserAsync();
		if (err is not null) return err;
		return FromResult(await _vault.AddAsync(user!, body.Address, body.Username, body.Password, HttpContext.RequestAborted));
	}

	public sealed record UpdateVaultBody(string? Username, string? Password);

	[HttpPut("/api/cloud/vault/{id:int}/")]
	public async Task<IActionResult> UpdateVault(int id, [FromBody] UpdateVaultBody body)
	{
		var (user, err) = await RequireUserAsync();
		if (err is not null) return err;
		return FromResult(await _vault.UpdateAsync(user!, id, body.Username, body.Password, HttpContext.RequestAborted));
	}

	[HttpDelete("/api/cloud/vault/{id:int}/")]
	public async Task<IActionResult> DeleteVault(int id)
	{
		var (user, err) = await RequireUserAsync();
		if (err is not null) return err;
		return FromResult(await _vault.DeleteAsync(user!, id, HttpContext.RequestAborted));
	}

	[HttpGet("/api/cloud/vault/{id:int}/secret/")]
	public async Task<IActionResult> RevealVault(int id)
	{
		var (user, err) = await RequireUserAsync();
		if (err is not null) return err;
		return FromResult(await _vault.RevealAsync(user!, id, HttpContext.RequestAborted));
	}

	// ---- 角色(人物卡) ----

	[HttpGet("/api/cloud/characters/")]
	public async Task<IActionResult> ListCharacters()
	{
		var (user, err) = await RequireUserAsync();
		if (err is not null) return err;
		return Ok(new { characters = await _characters.ListAsync(user!, HttpContext.RequestAborted) });
	}

	public sealed record CreateCharacterBody(string Name, string PasswordType, string? Password, List<string>? PasswordList, string? SkinUrl);

	[HttpPost("/api/cloud/characters/")]
	public async Task<IActionResult> CreateCharacter([FromBody] CreateCharacterBody body)
	{
		var (user, err) = await RequireUserAsync();
		if (err is not null) return err;

		if (!Enum.TryParse<CharacterPasswordType>(body.PasswordType, ignoreCase: true, out var passwordType))
			return ApiError(400, "password_type must be one of: FIXED, LIST_ROTATE, RANDOM");

		return FromResult(await _characters.CreateAsync(
			user!, body.Name, passwordType, body.Password, body.PasswordList, body.SkinUrl, HttpContext.RequestAborted));
	}

	public sealed record UpdateCharacterBody(string? Name, string? PasswordType, string? Password, List<string>? PasswordList, string? SkinUrl);

	[HttpPut("/api/cloud/characters/{id:int}/")]
	public async Task<IActionResult> UpdateCharacter(int id, [FromBody] UpdateCharacterBody body)
	{
		var (user, err) = await RequireUserAsync();
		if (err is not null) return err;

		CharacterPasswordType? passwordType = null;
		if (body.PasswordType is not null)
		{
			if (!Enum.TryParse<CharacterPasswordType>(body.PasswordType, ignoreCase: true, out var pt))
				return ApiError(400, "password_type must be one of: FIXED, LIST_ROTATE, RANDOM");
			passwordType = pt;
		}

		return FromResult(await _characters.UpdateAsync(
			user!, id, body.Name, passwordType, body.Password, body.PasswordList, body.SkinUrl, HttpContext.RequestAborted));
	}

	[HttpDelete("/api/cloud/characters/{id:int}/")]
	public async Task<IActionResult> DeleteCharacter(int id)
	{
		var (user, err) = await RequireUserAsync();
		if (err is not null) return err;
		return FromResult(await _characters.DeleteAsync(user!, id, HttpContext.RequestAborted));
	}

	[HttpGet("/api/cloud/characters/{id:int}/secret/")]
	public async Task<IActionResult> RevealCharacter(int id)
	{
		var (user, err) = await RequireUserAsync();
		if (err is not null) return err;
		return FromResult(await _characters.RevealAsync(user!, id, HttpContext.RequestAborted));
	}

	/// <summary>
	/// 上传角色皮肤(multipart 字段名 file;jpg/png/webp,≤2 MiB)。
	/// convert=auto(默认):64x64 MC 皮肤自动转换为 Luanti 64x32;convert=none 原样保存。
	/// </summary>
	[HttpPost("/api/cloud/characters/{id:int}/skin/")]
	public async Task<IActionResult> UploadSkin(int id, IFormFile file, [FromQuery] string convert = "auto")
	{
		var (user, err) = await RequireUserAsync();
		if (err is not null) return err;
		if (file is null || file.Length == 0) return ApiError(400, "file is required");
		return FromResult(await _characters.SetSkinAsync(
			user!, id, file.FileName, file.OpenReadStream(), file.Length, convert, HttpContext.RequestAborted));
	}

	/// <summary>导出角色皮肤为 Minecraft 64x64 PNG(64x32 自动补全左臂/左腿)。</summary>
	[HttpGet("/api/cloud/characters/{id:int}/skin/minecraft/")]
	public async Task<IActionResult> ExportMinecraftSkin(int id)
	{
		var (user, err) = await RequireUserAsync();
		if (err is not null) return err;

		var png = await _characters.ExportMinecraftSkinAsync(user!, id, HttpContext.RequestAborted);
		if (png is null) return ApiError(404, "Character or skin not found");
		return File(png, "image/png", $"character_{id}_mc_skin.png");
	}
}
