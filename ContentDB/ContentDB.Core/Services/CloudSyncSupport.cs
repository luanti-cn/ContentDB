// ContentDB C# —— 云同步辅助:服务器地址规范化 + Luanti 账户名规则

using System.Text.RegularExpressions;

namespace ContentDB.Core.Services;

public static partial class ServerAddress
{
	/// <summary>Luanti 默认端口(UDP)。</summary>
	public const int DefaultPort = 30000;

	[GeneratedRegex(@"^\[?([0-9A-Za-z._-]+?)\]?:(\d{1,5})$")]
	private static partial Regex HostPortRegex();

	/// <summary>
	/// 规范化服务器地址:去 scheme 与尾斜杠、host 小写、默认端口(30000)省略、IPv6 保持 [] 包裹。
	/// 例:"HTTP://Example.com:30000/" => "example.com";"[::1]:30001" => "[::1]:30001"。
	/// 无法解析时返回 null。
	/// </summary>
	public static string? Normalize(string? input)
	{
		if (string.IsNullOrWhiteSpace(input)) return null;

		var s = input.Trim().TrimEnd('/');
		foreach (var scheme in new[] { "udp://", "http://", "https://" })
		{
			if (s.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
			{
				s = s[scheme.Length..];
				break;
			}
		}
		if (s.Length == 0 || s.Length > 255) return null;

		// host:port 或 [ipv6]:port
		var m = HostPortRegex().Match(s);
		if (m.Success)
		{
			var host = m.Groups[1].Value.ToLowerInvariant();
			if (!int.TryParse(m.Groups[2].Value, out var port) || port is < 1 or > 65535) return null;
			if (port == DefaultPort) return host;
			// IPv6 继续用 [] 包裹,避免与端口分隔符混淆
			return host.Contains(':') ? $"[{host}]:{port}" : $"{host}:{port}";
		}

		// 纯 host(可能是裸 IPv6)
		s = s.ToLowerInvariant();
		return s.Contains(':') && !s.StartsWith('[') ? $"[{s}]" : s;
	}
}

public static partial class LuantiAccountName
{
	/// <summary>Luanti 账户名规则:1-20 个字母/数字/下划线/连字符。</summary>
	[GeneratedRegex(@"^[A-Za-z0-9_-]{1,20}$")]
	private static partial Regex NameRegex();

	public static bool IsValid(string? name) => !string.IsNullOrEmpty(name) && NameRegex().IsMatch(name);
}

/// <summary>为游戏服务器注册生成随机密码(无歧义字符集,密码学随机)。</summary>
public static class RandomPassword
{
	/// <summary>无歧义字母数字(不含 0/O、1/I/L)。</summary>
	public const string Alphabet = "23456789ABCDEFGHJKMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz";

	public static string Generate(int length = 16)
	{
		length = Math.Clamp(length, 8, 64);
		return new string(Enumerable.Range(0, length)
			.Select(_ => Alphabet[System.Security.Cryptography.RandomNumberGenerator.GetInt32(Alphabet.Length)])
			.ToArray());
	}
}
