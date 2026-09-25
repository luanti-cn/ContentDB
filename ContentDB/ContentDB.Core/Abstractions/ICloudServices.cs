// ContentDB C# —— 云同步服务抽象:保管库加密 / 设备配对 / 服务器账号保管库

using ContentDB.Core.Domain;

namespace ContentDB.Core.Abstractions;

/// <summary>保管库密码加密(AES-256-GCM)。</summary>
public interface IVaultCrypto
{
	string Encrypt(string plaintext);
	string Decrypt(string ciphertext);
}

public sealed record DeviceInfo(int Id, string Name, string TokenPrefix, DateTimeOffset CreatedAt, DateTimeOffset? LastUsedAt);

public sealed record PairingCreated(string Id, string Code, string DeepLink, DateTimeOffset ExpiresAt);

/// <summary>status: pending | claimed | expired。</summary>
public sealed record PairingStatus(string Status, DeviceInfo? Device);

/// <summary>配对成功响应(device token 明文仅此次返回)。</summary>
public sealed record PairedResult(string DeviceToken, int DeviceId, string DeviceName, string SiteUsername, string? DefaultServerUsername);

public sealed record VaultEntryInfo(
	int Id, string Address, string Username, string Status,
	DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record VaultSecret(int Id, string Address, string Username, string Password, DateTimeOffset UpdatedAt);

/// <summary>角色概要(不含密码)。</summary>
public sealed record CharacterInfo(
	int Id, string Name, string PasswordType, string? SkinUrl,
	DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

/// <summary>角色密码详情(网页查看)。</summary>
public sealed record CharacterSecret(
	int Id, string Name, string PasswordType, string? Password,
	int? RotateIndex, int? PasswordListCount, string? SkinUrl);

/// <summary>客户端按服务器配给凭证的结果。</summary>
public sealed record ProvisionedCredential(
	string Address, string Username, string Password, bool Created,
	int? CharacterId, string? CharacterName);

/// <summary>角色管理(网页端 CRUD + 客户端列表/配给)。</summary>
public interface IPlayerCharacterService
{
	/// <summary>列出角色(不含密码)。</summary>
	Task<IReadOnlyList<CharacterInfo>> ListAsync(User owner, CancellationToken ct = default);

	/// <summary>创建角色。FIXED 需 password;LIST_ROTATE 需 password_list(≥1 条);RANDOM 无需。</summary>
	Task<ServiceResult> CreateAsync(User owner, string name, CharacterPasswordType passwordType,
		string? password, IReadOnlyList<string>? passwordList, string? skinUrl, CancellationToken ct = default);

	/// <summary>更新角色(字段可空表示不改;切换密码类型时需带齐对应密码数据)。</summary>
	Task<ServiceResult> UpdateAsync(User owner, int id, string? name, CharacterPasswordType? passwordType,
		string? password, IReadOnlyList<string>? passwordList, string? skinUrl, CancellationToken ct = default);

	Task<ServiceResult> DeleteAsync(User owner, int id, CancellationToken ct = default);

	/// <summary>网页查看角色密码(FIXED=固定值;LIST_ROTATE=下一条;RANDOM=null)。</summary>
	Task<ServiceResult> RevealAsync(User owner, int id, CancellationToken ct = default);

	/// <summary>
	/// 上传角色皮肤图片(png/jpg/webp),存对象存储并更新 SkinUrl。
	/// convert=auto(默认):64x64 的 MC 皮肤自动转换为 Luanti 64x32(合并覆盖层);
	/// convert=none:按原样保存;64x32 直接保存。
	/// </summary>
	Task<ServiceResult> SetSkinAsync(User owner, int id, string fileName, Stream image, long length,
		string convert = "auto", CancellationToken ct = default);

	/// <summary>导出为 Minecraft 64x64 皮肤 PNG(64x32 自动补全左臂/左腿);无皮肤/外链皮肤返回 null。</summary>
	Task<byte[]?> ExportMinecraftSkinAsync(User owner, int id, CancellationToken ct = default);

	/// <summary>客户端:角色列表(不含密码)。</summary>
	Task<IReadOnlyList<CharacterInfo>> ListForClientAsync(User owner, CancellationToken ct = default);

	/// <summary>
	/// 客户端:按服务器配给凭证。已有凭证直接返回(Created=false);
	/// 否则按角色密码策略生成(characterId 为空时用默认用户名 + 随机密码),落库后返回。
	/// </summary>
	Task<ServiceResult> ProvisionAsync(User owner, string address, int? characterId, CancellationToken ct = default);
}

/// <summary>设备配对(网页端 + 客户端 claim)。</summary>
public interface IDevicePairingService
{
	Task<IReadOnlyList<DeviceInfo>> ListDevicesAsync(User owner, CancellationToken ct = default);

	/// <summary>创建配对会话,返回短码与 luanticn:// 深链(网页据此渲染二维码)。</summary>
	Task<ServiceResult> CreatePairingAsync(User owner, string deviceName, CancellationToken ct = default);

	/// <summary>网页轮询配对状态。</summary>
	Task<ServiceResult> GetPairingStatusAsync(User owner, string pairingId, CancellationToken ct = default);

	/// <summary>吊销设备(其 token 立即失效)。</summary>
	Task<ServiceResult> RevokeDeviceAsync(User owner, int deviceId, CancellationToken ct = default);

	/// <summary>客户端提交配对码换 device token(匿名,配对码即凭据)。</summary>
	Task<ServiceResult> ClaimAsync(string code, string? deviceName, CancellationToken ct = default);

	/// <summary>客户端心跳:上报当前所在服务器地址(联机在线状态;null 表示已离开)。</summary>
	Task TouchPresenceAsync(PairedDevice device, string? address, CancellationToken ct = default);
}

/// <summary>服务器账号保管库(网页端管理 + 客户端取用/回写)。</summary>
public interface ICloudVaultService
{
	/// <summary>设置进新服务器时使用的默认游戏内用户名(空串/null => 回落到站点用户名)。</summary>
	Task<ServiceResult> SetDefaultUsernameAsync(User owner, string? name, CancellationToken ct = default);

	/// <summary>列出凭证(不含密码)。</summary>
	Task<IReadOnlyList<VaultEntryInfo>> ListAsync(User owner, CancellationToken ct = default);

	/// <summary>网页手动添加。</summary>
	Task<ServiceResult> AddAsync(User owner, string address, string username, string password, CancellationToken ct = default);

	/// <summary>网页修改用户名/密码(字段可空表示不改)。</summary>
	Task<ServiceResult> UpdateAsync(User owner, int id, string? username, string? password, CancellationToken ct = default);

	Task<ServiceResult> DeleteAsync(User owner, int id, CancellationToken ct = default);

	/// <summary>网页查看明文密码。</summary>
	Task<ServiceResult> RevealAsync(User owner, int id, CancellationToken ct = default);

	/// <summary>客户端:用存储凭证登录失败时上报,标记为待更新(网页可见);改好后回写自动恢复 ACTIVE。</summary>
	Task<ServiceResult> MarkInvalidAsync(User owner, string address, CancellationToken ct = default);

	/// <summary>客户端:按服务器地址取凭证;无则返回 null(由控制器附默认用户名建议)。</summary>
	Task<VaultSecret?> FindForServerAsync(User owner, string address, CancellationToken ct = default);

	/// <summary>客户端:注册/改名成功后回写 upsert。</summary>
	Task<ServiceResult> UpsertForServerAsync(User owner, string address, string username, string password, CancellationToken ct = default);
}
