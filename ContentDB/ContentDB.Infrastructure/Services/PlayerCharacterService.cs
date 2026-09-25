// ContentDB C# —— 云端角色(人物卡)服务
// 角色 = 角色名 + 密码策略(固定/列表循环/随机)+ 皮肤。
// ProvisionAsync 是客户端进服的统一入口:已有凭证直接复用,否则按策略生成并落库。

using System.Text.Json;
using ContentDB.Core.Abstractions;
using ContentDB.Core.Configuration;
using ContentDB.Core.Domain;
using ContentDB.Core.Services;
using ContentDB.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;

namespace ContentDB.Infrastructure.Services;

public sealed class PlayerCharacterService : IPlayerCharacterService
{
	private const long MaxSkinBytes = 2 * 1024 * 1024;

	private readonly AppDbContext _db;
	private readonly IVaultCrypto _crypto;
	private readonly IObjectStorage _storage;
	private readonly VaultOptions _options;

	public PlayerCharacterService(AppDbContext db, IVaultCrypto crypto, IObjectStorage storage, IOptions<VaultOptions> options)
	{
		_db = db;
		_crypto = crypto;
		_storage = storage;
		_options = options.Value;
	}

	public async Task<IReadOnlyList<CharacterInfo>> ListAsync(User owner, CancellationToken ct = default)
	{
		return await _db.PlayerCharacters
			.Where(c => c.UserId == owner.Id)
			.OrderBy(c => c.Id)
			.Select(c => ToInfo(c))
			.ToListAsync(ct);
	}

	public async Task<ServiceResult> CreateAsync(User owner, string name, CharacterPasswordType passwordType,
		string? password, IReadOnlyList<string>? passwordList, string? skinUrl, CancellationToken ct = default)
	{
		name = (name ?? "").Trim();
		if (!LuantiAccountName.IsValid(name))
			return ServiceResult.Fail(400, "Invalid character name (Luanti username rules: 1-20 of A-Za-z0-9_-)");

		var maxChars = Math.Max(1, _options.MaxCharactersPerUser);
		var count = await _db.PlayerCharacters.CountAsync(c => c.UserId == owner.Id, ct);
		if (count >= maxChars)
			return ServiceResult.Fail(409, $"Character limit reached ({maxChars})");

		if (await _db.PlayerCharacters.AnyAsync(c => c.UserId == owner.Id && c.Name == name, ct))
			return ServiceResult.Fail(409, "Character name already exists");

		var character = new PlayerCharacter
		{
			UserId = owner.Id,
			Name = name,
			PasswordType = passwordType,
			SkinUrl = skinUrl,
		};

		if (await TrySetPasswordDataAsync(character, passwordType, password, passwordList) is { } err)
			return err;

		_db.PlayerCharacters.Add(character);
		await _db.SaveChangesAsync(ct);
		return ServiceResult.Ok(ToInfo(character));
	}

	public async Task<ServiceResult> UpdateAsync(User owner, int id, string? name, CharacterPasswordType? passwordType,
		string? password, IReadOnlyList<string>? passwordList, string? skinUrl, CancellationToken ct = default)
	{
		var character = await _db.PlayerCharacters
			.FirstOrDefaultAsync(c => c.Id == id && c.UserId == owner.Id, ct);
		if (character is null) return ServiceResult.Fail(404, "Character not found");

		if (name is not null)
		{
			name = name.Trim();
			if (!LuantiAccountName.IsValid(name))
				return ServiceResult.Fail(400, "Invalid character name (Luanti username rules)");
			if (name != character.Name
				&& await _db.PlayerCharacters.AnyAsync(c => c.UserId == owner.Id && c.Name == name && c.Id != id, ct))
				return ServiceResult.Fail(409, "Character name already exists");
			character.Name = name;
		}

		if (passwordType is not null)
			character.PasswordType = passwordType.Value;
		if (skinUrl is not null)
			character.SkinUrl = string.IsNullOrWhiteSpace(skinUrl) ? null : skinUrl.Trim();

		if (await TrySetPasswordDataAsync(character, character.PasswordType, password, passwordList, allowMissing: true) is { } err)
			return err;

		character.UpdatedAt = DateTimeOffset.UtcNow;
		await _db.SaveChangesAsync(ct);
		return ServiceResult.Ok(ToInfo(character));
	}

	public async Task<ServiceResult> DeleteAsync(User owner, int id, CancellationToken ct = default)
	{
		var character = await _db.PlayerCharacters
			.FirstOrDefaultAsync(c => c.Id == id && c.UserId == owner.Id, ct);
		if (character is null) return ServiceResult.Fail(404, "Character not found");

		_db.PlayerCharacters.Remove(character);
		await _db.SaveChangesAsync(ct);

		if (TryObjectKey(character.SkinUrl) is { } key)
			try { await _storage.DeleteAsync(key, ct); } catch { /* 孤儿对象可容忍 */ }
		return ServiceResult.Ok(new { success = true });
	}

	public async Task<ServiceResult> RevealAsync(User owner, int id, CancellationToken ct = default)
	{
		var character = await _db.PlayerCharacters
			.FirstOrDefaultAsync(c => c.Id == id && c.UserId == owner.Id, ct);
		if (character is null) return ServiceResult.Fail(404, "Character not found");

		string? password = null;
		int? rotateIndex = null;
		int? listCount = null;

		switch (character.PasswordType)
		{
			case CharacterPasswordType.FIXED:
				password = character.FixedPasswordEncrypted is null ? null : _crypto.Decrypt(character.FixedPasswordEncrypted);
				break;
			case CharacterPasswordType.LIST_ROTATE:
				if (character.PasswordListEncrypted is not null)
				{
					var list = DeserializeList(_crypto.Decrypt(character.PasswordListEncrypted));
					rotateIndex = list.Count > 0 ? character.RotateIndex % list.Count : 0;
					listCount = list.Count;
					password = list.Count > 0 ? list[rotateIndex.Value] : null;
				}
				break;
			case CharacterPasswordType.RANDOM:
				break;
		}

		return ServiceResult.Ok(new CharacterSecret(
			character.Id, character.Name, character.PasswordType.ToString(),
			password, rotateIndex, listCount, character.SkinUrl));
	}

	public async Task<ServiceResult> SetSkinAsync(User owner, int id, string fileName, Stream image, long length,
		string convert = "auto", CancellationToken ct = default)
	{
		var character = await _db.PlayerCharacters
			.FirstOrDefaultAsync(c => c.Id == id && c.UserId == owner.Id, ct);
		if (character is null) return ServiceResult.Fail(404, "Character not found");

		var ext = fileName.ToLowerInvariant() switch
		{
			var f when f.EndsWith(".png") => "png",
			var f when f.EndsWith(".jpg") || f.EndsWith(".jpeg") => "jpg",
			var f when f.EndsWith(".webp") => "webp",
			_ => null,
		};
		if (ext is null) return ServiceResult.Fail(400, "Unsupported image type (jpg/png/webp only)");
		if (length > MaxSkinBytes) return ServiceResult.Fail(413, "Image too large (max 2 MiB)");

		using var buffer = new MemoryStream();
		await image.CopyToAsync(buffer, ct);

		// 统一解码为 PNG:64x32(Luanti)原样重编码;64x64(MC)默认自动转换;其余尺寸拒绝
		byte[] skinPng;
		try
		{
			using var img = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Rgba32>(buffer.ToArray());
			if (SkinConverter.IsLuantiSkin(img.Width, img.Height))
			{
				skinPng = ToPng(img);
			}
			else if (SkinConverter.IsMinecraftSkin(img.Width, img.Height))
			{
				skinPng = convert == "none" ? ToPng(img) : SkinConverter.MinecraftToLuanti(img);
			}
			else
			{
				return ServiceResult.Fail(400, "Unsupported skin size (need 64x32 Luanti or 64x64 Minecraft)");
			}
		}
		catch (SixLabors.ImageSharp.UnknownImageFormatException)
		{
			return ServiceResult.Fail(400, "Invalid image file");
		}

		var key = $"uploads/skin_{RandomString(10)}.png";
		using (var content = new MemoryStream(skinPng))
		{
			await _storage.PutAsync(key, content, "image/png", ct);
		}

		var oldKey = TryObjectKey(character.SkinUrl);
		character.SkinUrl = "/" + key;
		character.UpdatedAt = DateTimeOffset.UtcNow;
		await _db.SaveChangesAsync(ct);

		if (oldKey is not null)
			try { await _storage.DeleteAsync(oldKey, ct); } catch { /* 孤儿对象可容忍 */ }

		return ServiceResult.Ok(new { id = character.Id, skin_url = character.SkinUrl, converted = skinPng.Length != buffer.Length });
	}

	public async Task<byte[]?> ExportMinecraftSkinAsync(User owner, int id, CancellationToken ct = default)
	{
		var character = await _db.PlayerCharacters
			.FirstOrDefaultAsync(c => c.Id == id && c.UserId == owner.Id, ct);
		if (character?.SkinUrl is null) return null;

		var key = TryObjectKey(character.SkinUrl);
		if (key is null) return null; // 外链皮肤无法读取转换

		await using var stream = await _storage.OpenReadAsync(key, ct);
		if (stream is null) return null;

		using var ms = new MemoryStream();
		await stream.CopyToAsync(ms, ct);

		using var img = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Rgba32>(ms.ToArray());
		return SkinConverter.IsLuantiSkin(img.Width, img.Height)
			? SkinConverter.LuantiToMinecraft(img)
			: ms.ToArray(); // 已是 MC 格式,原样返回
	}

	public async Task<IReadOnlyList<CharacterInfo>> ListForClientAsync(User owner, CancellationToken ct = default)
		=> await ListAsync(owner, ct);

	public async Task<ServiceResult> ProvisionAsync(User owner, string address, int? characterId, CancellationToken ct = default)
	{
		var normalized = ServerAddress.Normalize(address);
		if (normalized is null) return ServiceResult.Fail(400, "Invalid server address");

		// 已有凭证:直接复用(重复进同一服务器)
		var existing = await _db.ServerCredentials
			.FirstOrDefaultAsync(c => c.UserId == owner.Id && c.Address == normalized, ct);
		if (existing is not null)
			return ServiceResult.Ok(new ProvisionedCredential(
				normalized, existing.Username, _crypto.Decrypt(existing.PasswordEncrypted),
				Created: false, existing.CharacterId, null));

		PlayerCharacter? character = null;
		if (characterId is not null)
		{
			character = await _db.PlayerCharacters
				.FirstOrDefaultAsync(c => c.Id == characterId.Value && c.UserId == owner.Id, ct);
			if (character is null) return ServiceResult.Fail(404, "Character not found");
		}

		string username;
		string password;
		if (character is not null)
		{
			username = character.Name;
			switch (character.PasswordType)
			{
				case CharacterPasswordType.FIXED:
					if (character.FixedPasswordEncrypted is null)
						return ServiceResult.Fail(409, "Character has no fixed password set");
					password = _crypto.Decrypt(character.FixedPasswordEncrypted);
					break;
				case CharacterPasswordType.LIST_ROTATE:
				{
					if (character.PasswordListEncrypted is null)
						return ServiceResult.Fail(409, "Character has no password list set");
					var list = DeserializeList(_crypto.Decrypt(character.PasswordListEncrypted));
					if (list.Count == 0)
						return ServiceResult.Fail(409, "Character password list is empty");
					password = list[character.RotateIndex % list.Count];
					character.RotateIndex = (character.RotateIndex + 1) % list.Count;
					character.UpdatedAt = DateTimeOffset.UtcNow;
					break;
				}
				default:
					password = RandomPassword.Generate();
					break;
			}
		}
		else
		{
			username = owner.DefaultServerUsername ?? owner.Username;
			if (!LuantiAccountName.IsValid(username))
				return ServiceResult.Fail(409, "Default server username is not a valid Luanti username");
			password = RandomPassword.Generate();
		}

		var max = Math.Max(1, _options.MaxVaultEntriesPerUser);
		var count = await _db.ServerCredentials.CountAsync(c => c.UserId == owner.Id, ct);
		if (count >= max)
			return ServiceResult.Fail(409, $"Vault entry limit reached ({max})");

		var cred = new ServerCredential
		{
			UserId = owner.Id,
			Address = normalized,
			Username = username,
			PasswordEncrypted = _crypto.Encrypt(password),
			CharacterId = character?.Id,
			CreatedAt = DateTimeOffset.UtcNow,
			UpdatedAt = DateTimeOffset.UtcNow,
		};
		_db.ServerCredentials.Add(cred);
		await _db.SaveChangesAsync(ct);

		return ServiceResult.Ok(new ProvisionedCredential(
			normalized, username, password, Created: true, character?.Id, character?.Name));
	}

	// ---- 内部 ----

	/// <summary>按密码类型校验并写入密码数据。切换类型时未带数据且原数据缺失则报错(allowMissing 用于不切换场景)。</summary>
	private async Task<ServiceResult?> TrySetPasswordDataAsync(
		PlayerCharacter character, CharacterPasswordType type, string? password, IReadOnlyList<string>? passwordList,
		bool allowMissing = false)
	{
		var maxEntries = Math.Max(1, _options.MaxPasswordListEntries);
		switch (type)
		{
			case CharacterPasswordType.FIXED:
				if (password is not null)
				{
					if (password.Length is 0 or > 100)
						return ServiceResult.Fail(400, "password must be 1-100 chars");
					character.FixedPasswordEncrypted = _crypto.Encrypt(password);
				}
				else if (character.FixedPasswordEncrypted is null && !allowMissing)
				{
					return ServiceResult.Fail(400, "password is required for FIXED");
				}
				break;

			case CharacterPasswordType.LIST_ROTATE:
				if (passwordList is not null)
				{
					var items = passwordList
						.Where(p => !string.IsNullOrEmpty(p) && p.Length <= 100)
						.Distinct().ToList();
					if (items.Count == 0)
						return ServiceResult.Fail(400, "password_list must contain at least 1 valid entry (1-100 chars each)");
					if (items.Count > maxEntries)
						return ServiceResult.Fail(400, $"password_list supports up to {maxEntries} entries");
					character.PasswordListEncrypted = _crypto.Encrypt(JsonSerializer.Serialize(items));
					character.RotateIndex = 0;
				}
				else if (character.PasswordListEncrypted is null && !allowMissing)
				{
					return ServiceResult.Fail(400, "password_list is required for LIST_ROTATE");
				}
				break;

			case CharacterPasswordType.RANDOM:
				break;
		}

		character.UpdatedAt = DateTimeOffset.UtcNow;
		await Task.CompletedTask;
		return null;
	}

	private static List<string> DeserializeList(string json)
		=> JsonSerializer.Deserialize<List<string>>(json) ?? [];

	private static CharacterInfo ToInfo(PlayerCharacter c)
		=> new(c.Id, c.Name, c.PasswordType.ToString(), c.SkinUrl, c.CreatedAt, c.UpdatedAt);

	/// <summary>/uploads/... URL -> 对象存储 key;其余(外链)返回 null。</summary>
	private static string? TryObjectKey(string? url)
		=> url is not null && url.StartsWith("/uploads/", StringComparison.Ordinal) ? url.TrimStart('/') : null;

	private static byte[] ToPng(SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32> img)
	{
		using var ms = new MemoryStream();
		img.SaveAsPng(ms);
		return ms.ToArray();
	}

	private static string RandomString(int len)
	{
		const string chars = "abcdefghijklmnopqrstuvwxyz0123456789";
		return new string(Enumerable.Range(0, len).Select(_ => chars[Random.Shared.Next(chars.Length)]).ToArray());
	}
}
