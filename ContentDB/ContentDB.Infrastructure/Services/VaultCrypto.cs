// ContentDB C# —— 保管库密码加密(AES-256-GCM)
// 密文格式:v1 = [0x01][12B nonce][16B tag][cipher],整体 base64。
// 密钥来自 Vault:EncryptionKey(base64 的 32 字节)。

using System.Security.Cryptography;
using System.Text;
using ContentDB.Core.Abstractions;
using ContentDB.Core.Configuration;
using Microsoft.Extensions.Options;

namespace ContentDB.Infrastructure.Services;

public sealed class VaultCrypto : IVaultCrypto
{
	private const int NonceSize = 12;
	private const int TagSize = 16;
	private const byte Version = 1;

	private readonly byte[] _key;

	public VaultCrypto(IOptions<VaultOptions> options)
	{
		var raw = options.Value.EncryptionKey;
		if (string.IsNullOrWhiteSpace(raw))
			throw new InvalidOperationException(
				"Vault:EncryptionKey 未配置(需为 32 字节的 base64)。生产环境请用环境变量/User Secrets 注入。");
		_key = Convert.FromBase64String(raw);
		if (_key.Length != 32)
			throw new InvalidOperationException("Vault:EncryptionKey 必须正好解码为 32 字节(AES-256)。");
	}

	public string Encrypt(string plaintext)
	{
		var nonce = RandomNumberGenerator.GetBytes(NonceSize);
		var plain = Encoding.UTF8.GetBytes(plaintext);
		var cipher = new byte[plain.Length];
		var tag = new byte[TagSize];
		using var gcm = new AesGcm(_key, TagSize);
		gcm.Encrypt(nonce, plain, cipher, tag);

		var result = new byte[1 + NonceSize + TagSize + cipher.Length];
		result[0] = Version;
		nonce.CopyTo(result, 1);
		tag.CopyTo(result, 1 + NonceSize);
		cipher.CopyTo(result, 1 + NonceSize + TagSize);
		return Convert.ToBase64String(result);
	}

	public string Decrypt(string ciphertext)
	{
		var data = Convert.FromBase64String(ciphertext);
		if (data.Length < 1 + NonceSize + TagSize || data[0] != Version)
			throw new CryptographicException("未知的保管库密文格式");
		var nonce = data.AsSpan(1, NonceSize);
		var tag = data.AsSpan(1 + NonceSize, TagSize);
		var cipher = data.AsSpan(1 + NonceSize + TagSize);
		var plain = new byte[cipher.Length];
		using var gcm = new AesGcm(_key, TagSize);
		gcm.Decrypt(nonce, cipher, tag, plain);
		return Encoding.UTF8.GetString(plain);
	}
}
