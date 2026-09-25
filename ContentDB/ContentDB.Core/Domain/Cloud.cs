// ContentDB C# —— 云同步:设备配对 / 服务器账号保管库
// 设备通过一次性配对码绑定到账号,换取长期 device token;
// 之后客户端可在进服务器前从云端取回该服务器的用户名/密码(加密存储)。

namespace ContentDB.Core.Domain;

/// <summary>用户绑定的设备(Luanti 客户端/启动器)。长期 token 只存哈希。</summary>
public class PairedDevice
{
	public int Id { get; set; }

	public int UserId { get; set; }
	public User User { get; set; } = null!;

	/// <summary>设备名(用户在网页上起,客户端配对时可覆盖)。</summary>
	public string Name { get; set; } = "";

	/// <summary>device token 的 SHA-256 十六进制哈希(明文仅配对成功时返回一次)。</summary>
	public string TokenHash { get; set; } = "";

	/// <summary>token 明文前 8 个字符,用于网页端识别设备。</summary>
	public string TokenPrefix { get; set; } = "";

	public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
	public DateTimeOffset? LastUsedAt { get; set; }
	public DateTimeOffset? RevokedAt { get; set; }

	// ---- 在线状态(联机)----
	/// <summary>客户端心跳上报的当前所在服务器地址(空表示不在服务器)。</summary>
	public string? CurrentServerAddress { get; set; }

	/// <summary>最近一次心跳时间;超过窗口(约 5 分钟)视为离线。</summary>
	public DateTimeOffset? PresenceUpdatedAt { get; set; }

	public bool IsActive => RevokedAt is null;
}

/// <summary>一次性配对会话:网页创建短码,客户端提交短码换取 device token。</summary>
public class DevicePairing
{
	/// <summary>GUID,供网页轮询配对状态。</summary>
	public string Id { get; set; } = "";

	public int UserId { get; set; }
	public User User { get; set; } = null!;

	/// <summary>网页上预设的设备名(客户端配对时可传新名字覆盖)。</summary>
	public string DeviceName { get; set; } = "";

	/// <summary>短配对码(形如 XXXX-XXXX,无歧义字符集),即配对密钥本身。</summary>
	public string Code { get; set; } = "";

	public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
	public DateTimeOffset ExpiresAt { get; set; }

	public DateTimeOffset? ClaimedAt { get; set; }
	public int? DeviceId { get; set; }
	public PairedDevice? Device { get; set; }

	public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAt;
}

/// <summary>角色密码策略。</summary>
public enum CharacterPasswordType
{
	/// <summary>固定密码(用户自己设定)。</summary>
	FIXED,

	/// <summary>密码列表循环:每配给一个新服务器取下一条,用完从头循环。</summary>
	LIST_ROTATE,

	/// <summary>每个新服务器随机生成一条。</summary>
	RANDOM,
}

/// <summary>
/// 云端角色(人物卡):角色名 + 密码策略 + 皮肤。
/// 进新服务器时客户端可指定用某个角色配给凭证;之后每台服务器的实际
/// 用户名/密码以保管库(ServerCredential)为准(改名/改密后与角色无关)。
/// </summary>
public class PlayerCharacter
{
	public int Id { get; set; }

	public int UserId { get; set; }
	public User User { get; set; } = null!;

	/// <summary>角色名(即默认游戏内用户名,Luanti 账户名规则)。</summary>
	public string Name { get; set; } = "";

	public CharacterPasswordType PasswordType { get; set; } = CharacterPasswordType.RANDOM;

	/// <summary>FIXED:固定密码(AES-256-GCM)。</summary>
	public string? FixedPasswordEncrypted { get; set; }

	/// <summary>LIST_ROTATE:密码列表(JSON 字符串数组整体加密)。</summary>
	public string? PasswordListEncrypted { get; set; }

	/// <summary>LIST_ROTATE:下一条密码的游标(配给新服务器后 +1,循环)。</summary>
	public int RotateIndex { get; set; }

	/// <summary>角色皮肤图片 URL(/uploads/... 本地上传,或外部 http(s) 链接)。</summary>
	public string? SkinUrl { get; set; }

	public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
	public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>凭证状态。客户端用存储密码登录失败时上报 NEEDS_UPDATE,改好后回 ACTIVE。</summary>
public enum CredentialStatus
{
	ACTIVE,
	NEEDS_UPDATE,
}

/// <summary>
/// 云端账号保管库:用户在某游戏服务器上的登录凭证。
/// 密码以 AES-256-GCM 加密落库(Vault:EncryptionKey)。
/// </summary>
public class ServerCredential
{
	public int Id { get; set; }

	public int UserId { get; set; }
	public User User { get; set; } = null!;

	/// <summary>规范化的服务器地址(host[:port],默认端口 30000 省略)。</summary>
	public string Address { get; set; } = "";

	/// <summary>该服务器上的游戏内用户名。</summary>
	public string Username { get; set; } = "";

	public string PasswordEncrypted { get; set; } = "";

	/// <summary>ACTIVE 正常;NEEDS_UPDATE 表示客户端上报过登录失败(密码可能在游戏内被改)。</summary>
	public CredentialStatus Status { get; set; } = CredentialStatus.ACTIVE;

	/// <summary>最近一次登录失败上报时间(可空)。</summary>
	public DateTimeOffset? InvalidReportedAt { get; set; }

	/// <summary>配给时使用的角色(可空;服务器上改名后与角色脱钩,仅作来源标记)。</summary>
	public int? CharacterId { get; set; }
	public PlayerCharacter? Character { get; set; }

	public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
	public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
