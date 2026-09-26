// ContentDB C# —— WebSocket 连接票据
// 网页端跨域场景(如 Cloudflare 托管前端)无法靠 cookie 完成 WS 握手认证:
// 先用会话/设备 token 调 REST 换一次性短时 ticket,再以 ?ticket= 建立 WS。
// 单例内存表,票据 60 秒有效、用后即焚,惰性清扫。

using System.Collections.Concurrent;

namespace ContentDB.Infrastructure.Realtime;

public sealed class WsTicketStore
{
	private sealed record Entry(int UserId, int? DeviceId, DateTimeOffset ExpiresAt);

	private readonly ConcurrentDictionary<string, Entry> _tickets = [];

	public string Issue(int userId, int? deviceId)
	{
		Sweep();
		Span<byte> bytes = stackalloc byte[32];
		System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
		var ticket = Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
		_tickets[ticket] = new Entry(userId, deviceId, DateTimeOffset.UtcNow.AddSeconds(60));
		return ticket;
	}

	/// <summary>取票(一次性);过期/不存在返回 null。</summary>
	public (int UserId, int? DeviceId)? TryTake(string? ticket)
	{
		if (string.IsNullOrEmpty(ticket) || ticket.Length < 20) return null;
		if (!_tickets.TryRemove(ticket, out var entry)) return null;
		if (entry.ExpiresAt < DateTimeOffset.UtcNow) return null;
		return (entry.UserId, entry.DeviceId);
	}

	private void Sweep()
	{
		var now = DateTimeOffset.UtcNow;
		foreach (var (key, entry) in _tickets)
			if (entry.ExpiresAt < now) _tickets.TryRemove(key, out _);
	}
}
