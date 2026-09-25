// ContentDB C# —— 设备配对服务
// 网页创建一次性短配对码(即配对密钥),客户端提交短码换取长期 device token。

using System.Security.Cryptography;
using ContentDB.Core.Abstractions;
using ContentDB.Core.Configuration;
using ContentDB.Core.Domain;
using ContentDB.Core.Services;
using ContentDB.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ContentDB.Infrastructure.Services;

public sealed class DevicePairingService : IDevicePairingService
{
	/// <summary>短码字符集:去掉 0/O、1/I/L 的易混淆字符。</summary>
	private const string CodeAlphabet = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";

	private static readonly TimeSpan PairingTtlFallback = TimeSpan.FromMinutes(10);

	private readonly AppDbContext _db;
	private readonly INotificationService _notifications;
	private readonly VaultOptions _options;

	public DevicePairingService(AppDbContext db, INotificationService notifications, IOptions<VaultOptions> options)
	{
		_db = db;
		_notifications = notifications;
		_options = options.Value;
	}

	public async Task<IReadOnlyList<DeviceInfo>> ListDevicesAsync(User owner, CancellationToken ct = default)
	{
		return await _db.PairedDevices
			.Where(d => d.UserId == owner.Id && d.RevokedAt == null)
			.OrderByDescending(d => d.LastUsedAt ?? d.CreatedAt)
			.Select(d => new DeviceInfo(d.Id, d.Name, d.TokenPrefix, d.CreatedAt, d.LastUsedAt))
			.ToListAsync(ct);
	}

	public async Task<ServiceResult> CreatePairingAsync(User owner, string deviceName, CancellationToken ct = default)
	{
		deviceName = (deviceName ?? "").Trim();
		if (deviceName.Length is 0 or > 60)
			return ServiceResult.Fail(400, "device_name is required (1-60 chars)");

		// 顺手清理该用户已过期/已完成的历史配对会话
		var stale = await _db.DevicePairings
			.Where(p => p.UserId == owner.Id && (p.ExpiresAt < DateTimeOffset.UtcNow || p.ClaimedAt != null))
			.ToListAsync(ct);
		_db.DevicePairings.RemoveRange(stale);

		var ttl = _options.PairingCodeTtlMinutes > 0
			? TimeSpan.FromMinutes(_options.PairingCodeTtlMinutes)
			: PairingTtlFallback;

		var pairing = new DevicePairing
		{
			Id = Guid.NewGuid().ToString("N"),
			UserId = owner.Id,
			DeviceName = deviceName,
			Code = await GenerateUniqueCodeAsync(ct),
			CreatedAt = DateTimeOffset.UtcNow,
			ExpiresAt = DateTimeOffset.UtcNow + ttl,
		};
		_db.DevicePairings.Add(pairing);
		await _db.SaveChangesAsync(ct);

		return ServiceResult.Ok(new PairingCreated(pairing.Id, pairing.Code, BuildDeepLink(pairing.Code), pairing.ExpiresAt));
	}

	public async Task<ServiceResult> GetPairingStatusAsync(User owner, string pairingId, CancellationToken ct = default)
	{
		var pairing = await _db.DevicePairings
			.Include(p => p.Device)
			.FirstOrDefaultAsync(p => p.Id == pairingId && p.UserId == owner.Id, ct);
		if (pairing is null) return ServiceResult.Fail(404, "Pairing not found");

		if (pairing.ClaimedAt is not null)
			return ServiceResult.Ok(new PairingStatus("claimed", pairing.Device is null ? null : new DeviceInfo(
				pairing.Device.Id, pairing.Device.Name, pairing.Device.TokenPrefix,
				pairing.Device.CreatedAt, pairing.Device.LastUsedAt)));
		if (pairing.IsExpired)
			return ServiceResult.Ok(new PairingStatus("expired", null));
		return ServiceResult.Ok(new PairingStatus("pending", null));
	}

	public async Task<ServiceResult> RevokeDeviceAsync(User owner, int deviceId, CancellationToken ct = default)
	{
		var device = await _db.PairedDevices
			.FirstOrDefaultAsync(d => d.Id == deviceId && d.UserId == owner.Id && d.RevokedAt == null, ct);
		if (device is null) return ServiceResult.Fail(404, "Device not found");

		device.RevokedAt = DateTimeOffset.UtcNow;
		await _db.SaveChangesAsync(ct);
		return ServiceResult.Ok(new { success = true });
	}

	public async Task<ServiceResult> ClaimAsync(string code, string? deviceName, CancellationToken ct = default)
	{
		var normalized = NormalizeCode(code);
		if (normalized is null) return ServiceResult.Fail(400, "code is required (format XXXX-XXXX)");

		var pairing = await _db.DevicePairings
			.Include(p => p.User)
			.FirstOrDefaultAsync(p => p.Code == normalized, ct);
		if (pairing is null) return ServiceResult.Fail(404, "Unknown pairing code");
		if (pairing.ClaimedAt is not null) return ServiceResult.Fail(410, "Pairing code already used");
		if (pairing.IsExpired) return ServiceResult.Fail(410, "Pairing code expired");

		var maxDevices = Math.Max(1, _options.MaxDevicesPerUser);
		var deviceCount = await _db.PairedDevices.CountAsync(d => d.UserId == pairing.UserId && d.RevokedAt == null, ct);
		if (deviceCount >= maxDevices)
			return ServiceResult.Fail(409, $"Device limit reached ({maxDevices}); revoke a device first");

		var token = GenerateToken();
		var device = new PairedDevice
		{
			UserId = pairing.UserId,
			Name = string.IsNullOrWhiteSpace(deviceName) ? pairing.DeviceName : deviceName.Trim(),
			TokenHash = HashToken(token),
			TokenPrefix = token[..8],
			CreatedAt = DateTimeOffset.UtcNow,
			LastUsedAt = DateTimeOffset.UtcNow,
		};
		_db.PairedDevices.Add(device);

		pairing.ClaimedAt = DateTimeOffset.UtcNow;
		// 用导航属性建立关联:EF 会先插 paired_device 取回自增 Id,再写 device_pairing.DeviceId。
		// 若直接设 pairing.DeviceId = device.Id,此时 Id 还是 0,会触发外键违规。
		pairing.Device = device;
		await _db.SaveChangesAsync(ct);

		return ServiceResult.Ok(new PairedResult(
			token, device.Id, device.Name, pairing.User.Username, pairing.User.DefaultServerUsername));
	}

	public async Task TouchPresenceAsync(PairedDevice device, string? address, CancellationToken ct = default)
	{
		var wasOnline = device.PresenceUpdatedAt is not null
			&& DateTimeOffset.UtcNow - device.PresenceUpdatedAt.Value < PresenceLookup.OnlineWindow;

		device.CurrentServerAddress = ServerAddress.Normalize(address);
		device.PresenceUpdatedAt = DateTimeOffset.UtcNow;
		await _db.SaveChangesAsync(ct);

		// 离线 → 上线(开始游戏)时通知好友;持续心跳不会重复通知
		if (!wasOnline && device.CurrentServerAddress is not null)
		{
			var friendIds = await _db.FriendLinks
				.Where(l => l.Status == FriendLinkStatus.ACCEPTED
					&& (l.RequesterId == device.UserId || l.AddresseeId == device.UserId))
				.Select(l => l.RequesterId == device.UserId ? l.AddresseeId : l.RequesterId)
				.ToListAsync(ct);
			foreach (var friendId in friendIds)
				await _notifications.NotifyAsync(friendId, device.UserId, NotificationType.FRIEND_ONLINE,
					$"{device.User.Username} 上线了,正在 {device.CurrentServerAddress}", "/friends", ct: ct);
		}
	}

	public static string HashToken(string token)
	{
		var hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token));
		return Convert.ToHexString(hash).ToLowerInvariant();
	}

	/// <summary>配对码即密钥:纯码(不含连字符)参与查询,入库时统一为 XXXX-XXXX 展示形态。</summary>
	private static string? NormalizeCode(string? code)
	{
		if (string.IsNullOrWhiteSpace(code)) return null;
		var compact = code.Replace("-", "").Replace(" ", "").ToUpperInvariant();
		return compact.Length != 8 || compact.Any(c => !CodeAlphabet.Contains(c)) ? null : $"{compact[..4]}-{compact[4..]}";
	}

	private async Task<string> GenerateUniqueCodeAsync(CancellationToken ct)
	{
		for (var attempt = 0; attempt < 5; attempt++)
		{
			var code = new string(Enumerable.Range(0, 8)
				.Select(_ => CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)])
				.ToArray());
			var formatted = $"{code[..4]}-{code[4..]}";
			if (!await _db.DevicePairings.AnyAsync(p => p.Code == formatted, ct))
				return formatted;
		}
		throw new InvalidOperationException("无法生成唯一配对码,请重试");
	}

	private static string BuildDeepLink(string code) => $"luanticn://login?code={code}";

	private static string GenerateToken()
	{
		Span<byte> bytes = stackalloc byte[48];
		RandomNumberGenerator.Fill(bytes);
		return Convert.ToBase64String(bytes)
			.Replace('+', '-').Replace('/', '_').TrimEnd('=');
	}
}
