// ContentDB C# —— 在线状态查询:给定一批用户 id,返回各自最近一次有效心跳。

using ContentDB.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContentDB.Infrastructure.Services;

public static class PresenceLookup
{
	/// <summary>心跳有效期窗口,超过即视为离线。</summary>
	public static readonly TimeSpan OnlineWindow = TimeSpan.FromMinutes(5);

	public static async Task<Dictionary<int, (DateTimeOffset? At, string? Server)>> LookupAsync(
		AppDbContext db, IReadOnlyList<int> userIds, CancellationToken ct = default)
	{
		if (userIds.Count == 0) return [];

		var cutoff = DateTimeOffset.UtcNow - OnlineWindow;
		var rows = await db.PairedDevices
			.Where(d => userIds.Contains(d.UserId) && d.PresenceUpdatedAt != null && d.PresenceUpdatedAt > cutoff)
			.OrderByDescending(d => d.PresenceUpdatedAt)
			.Select(d => new { d.UserId, d.PresenceUpdatedAt, d.CurrentServerAddress })
			.ToListAsync(ct);

		return rows.GroupBy(p => p.UserId)
			.ToDictionary(
				g => g.Key,
				g => ((DateTimeOffset?)g.First().PresenceUpdatedAt, g.First().CurrentServerAddress));
	}
}
