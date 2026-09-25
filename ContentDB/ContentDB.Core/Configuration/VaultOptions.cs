// ContentDB C# —— 云同步(设备配对/账号保管库)配置

namespace ContentDB.Core.Configuration;

public sealed class VaultOptions
{
	public const string SectionName = "Vault";

	/// <summary>
	/// AES-256-GCM 密钥(32 字节的 base64)。
	/// 生产环境务必通过环境变量 / User Secrets 注入;更换密钥会导致已存密码无法解密。
	/// </summary>
	public string EncryptionKey { get; set; } = "";

	/// <summary>配对码有效期(分钟)。</summary>
	public int PairingCodeTtlMinutes { get; set; } = 10;

	/// <summary>每用户最多绑定的设备数。</summary>
	public int MaxDevicesPerUser { get; set; } = 20;

	/// <summary>每用户最多保存的服务器凭证数。</summary>
	public int MaxVaultEntriesPerUser { get; set; } = 200;

	/// <summary>每用户最多创建的角色数。</summary>
	public int MaxCharactersPerUser { get; set; } = 50;

	/// <summary>LIST_ROTATE 密码列表最大条数。</summary>
	public int MaxPasswordListEntries { get; set; } = 50;
}
