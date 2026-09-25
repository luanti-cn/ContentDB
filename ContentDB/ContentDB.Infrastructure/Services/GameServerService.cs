// ContentDB C# —— 游戏服务器大厅服务
// 服主注册收录,凭上报 token 由服务器端 mod 定期推送实时状态;
// 列表附带"本站用户在线数"(来自配对设备心跳的 CurrentServerAddress 汇总)。

using System.Security.Cryptography;
using ContentDB.Core.Abstractions;
using ContentDB.Core.Domain;
using ContentDB.Core.Services;
using ContentDB.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContentDB.Infrastructure.Services;

public sealed class GameServerService : IGameServerService
{
	private const string TokenAlphabet = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";

	private readonly AppDbContext _db;

	public GameServerService(AppDbContext db) => _db = db;

	public async Task<IReadOnlyList<GameServerInfo>> ListAsync(CancellationToken ct = default)
	{
		var servers = await _db.GameServers
			.Include(s => s.Owner)
			.Where(s => s.Listed)
			.OrderByDescending(s => s.Verified)
			.ThenByDescending(s => s.ReportedAt)
			.ToListAsync(ct);
		if (servers.Count == 0) return [];

		// 本站用户在线数:按当前所在服务器汇总心跳
		var cutoff = DateTimeOffset.UtcNow - PresenceLookup.OnlineWindow;
		var addresses = servers.Select(s => s.Address).ToList();
		var ours = await _db.PairedDevices
			.Where(d => d.CurrentServerAddress != null && d.PresenceUpdatedAt != null && d.PresenceUpdatedAt > cutoff)
			.Select(d => new { d.CurrentServerAddress })
			.ToListAsync(ct);
		var counts = ours
			.GroupBy(d => d.CurrentServerAddress!)
			.ToDictionary(g => g.Key, g => g.Count());

		return servers
			.Select(s => new GameServerInfo(
				s.Id, s.Address, s.Name, s.Description, s.WebsiteUrl,
				s.Owner.Username, s.PlayersOnline, s.PlayersMax, s.IsOnline, s.Verified,
				counts.TryGetValue(s.Address, out var n) ? n : 0, s.ReportedAt))
			.ToList();
	}

	public Task<IReadOnlyList<MyGameServerInfo>> ListMineAsync(User owner, CancellationToken ct = default)
		=> ListMineCoreAsync(owner.Id, ct);

	private async Task<IReadOnlyList<MyGameServerInfo>> ListMineCoreAsync(int ownerId, CancellationToken ct)
	{
		return await _db.GameServers
			.Where(s => s.OwnerId == ownerId)
			.OrderBy(s => s.Id)
			.Select(s => new MyGameServerInfo(
				s.Id, s.Address, s.Name, s.Description, s.WebsiteUrl,
				s.PlayersOnline, s.PlayersMax, s.IsOnline, s.Listed, s.Verified,
				s.ReportTokenPrefix, s.CreatedAt))
			.ToListAsync(ct);
	}

	public async Task<ServiceResult> RegisterAsync(User actor, string address, string name,
		string? description, string? websiteUrl, CancellationToken ct = default)
	{
		var normalized = ServerAddress.Normalize(address);
		if (normalized is null) return ServiceResult.Fail(400, "Invalid server address");

		name = (name ?? "").Trim();
		if (name.Length is 0 or > 60) return ServiceResult.Fail(400, "name is required (1-60 chars)");
		description = description?.Trim() is { Length: > 0 } d ? d : null;
		if (description is { Length: > 500 }) return ServiceResult.Fail(400, "description too long (max 500)");
		websiteUrl = websiteUrl?.Trim() is { Length: > 0 } w ? w : null;

		if (await _db.GameServers.AnyAsync(s => s.Address == normalized, ct))
			return ServiceResult.Fail(409, "Server already registered");

		var server = new GameServer
		{
			Address = normalized,
			Name = name,
			Description = description,
			WebsiteUrl = websiteUrl,
			OwnerId = actor.Id,
		};
		var token = GenerateToken();
		ApplyReportToken(server, token);
		_db.GameServers.Add(server);
		await _db.SaveChangesAsync(ct);

		return ServiceResult.Ok(new ServerReportToken(server.Id, server.Address, token));
	}

	public async Task<ServiceResult> UpdateAsync(User actor, int id, string? name, string? description,
		string? websiteUrl, bool? listed, CancellationToken ct = default)
	{
		var server = await _db.GameServers
			.FirstOrDefaultAsync(s => s.Id == id && s.OwnerId == actor.Id, ct);
		if (server is null) return ServiceResult.Fail(404, "Server not found");

		if (name is not null)
		{
			name = name.Trim();
			if (name.Length is 0 or > 60) return ServiceResult.Fail(400, "name must be 1-60 chars");
			server.Name = name;
		}
		if (description is not null)
			server.Description = description.Trim() is { Length: > 0 } d && d.Length <= 500 ? d : null;
		if (websiteUrl is not null)
			server.WebsiteUrl = websiteUrl.Trim() is { Length: > 0 } w ? w : null;
		if (listed is not null)
			server.Listed = listed.Value;

		await _db.SaveChangesAsync(ct);
		return ServiceResult.Ok(new { success = true });
	}

	public async Task<ServiceResult> DeleteAsync(User actor, int id, CancellationToken ct = default)
	{
		var server = await _db.GameServers
			.FirstOrDefaultAsync(s => s.Id == id && s.OwnerId == actor.Id, ct);
		if (server is null) return ServiceResult.Fail(404, "Server not found");

		_db.GameServers.Remove(server);
		await _db.SaveChangesAsync(ct);
		return ServiceResult.Ok(new { success = true });
	}

	public async Task<ServiceResult> RegenerateReportTokenAsync(User actor, int id, CancellationToken ct = default)
	{
		var server = await _db.GameServers
			.FirstOrDefaultAsync(s => s.Id == id && s.OwnerId == actor.Id, ct);
		if (server is null) return ServiceResult.Fail(404, "Server not found");

		var token = GenerateToken();
		ApplyReportToken(server, token);
		await _db.SaveChangesAsync(ct);

		return ServiceResult.Ok(new ServerReportToken(server.Id, server.Address, token));
	}

	public async Task<ServiceResult> ReportStatusAsync(string address, string token,
		int playersOnline, int playersMax, string? motd, CancellationToken ct = default)
	{
		var normalized = ServerAddress.Normalize(address);
		if (normalized is null) return ServiceResult.Fail(400, "Invalid server address");
		if (string.IsNullOrWhiteSpace(token)) return ServiceResult.Fail(401, "Report token required");

		var server = await _db.GameServers.FirstOrDefaultAsync(s => s.Address == normalized, ct);
		if (server is null || server.ReportTokenHash is null) return ServiceResult.Fail(404, "Server not registered");
		if (server.ReportTokenHash != HashToken(token)) return ServiceResult.Fail(403, "Invalid report token");

		server.PlayersOnline = Math.Clamp(playersOnline, 0, 10_000);
		server.PlayersMax = Math.Clamp(playersMax, 0, 10_000);
		server.Motd = motd is { Length: > 0 } ? motd[..Math.Min(motd.Length, 200)] : null;
		server.ReportedAt = DateTimeOffset.UtcNow;
		await _db.SaveChangesAsync(ct);
		return ServiceResult.Ok(new { success = true });
	}

	// ---- 管理员 ----

	public async Task<IReadOnlyList<AdminGameServerInfo>> AdminListAsync(CancellationToken ct = default)
	{
		return await _db.GameServers.AsNoTracking()
			.Include(s => s.Owner)
			.OrderByDescending(s => s.CreatedAt)
			.Select(s => new AdminGameServerInfo(
				s.Id, s.Address, s.Name, s.Owner.Username, s.Listed, s.Verified,
				s.PlayersOnline, s.PlayersMax,
				s.ReportedAt != null
					&& DateTimeOffset.UtcNow - s.ReportedAt.Value < TimeSpan.FromMinutes(10),
				s.CreatedAt))
			.ToListAsync(ct);
	}

	public async Task<ServiceResult> AdminSetFlagsAsync(User actor, int id, bool? verified, bool? listed,
		CancellationToken ct = default)
	{
		if (!actor.Rank.AtLeast(UserRank.MODERATOR))
			return ServiceResult.Fail(403, "Moderator privileges required");

		var server = await _db.GameServers.FirstOrDefaultAsync(s => s.Id == id, ct);
		if (server is null) return ServiceResult.Fail(404, "Server not found");

		if (verified is not null) server.Verified = verified.Value;
		if (listed is not null) server.Listed = listed.Value;
		await _db.SaveChangesAsync(ct);
		return ServiceResult.Ok(new { success = true });
	}

	// ---- 内部 ----

	private static void ApplyReportToken(GameServer server, string token)
	{
		server.ReportTokenHash = HashToken(token);
		server.ReportTokenPrefix = token[..8];
	}

	private static string HashToken(string token)
	{
		var hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token));
		return Convert.ToHexString(hash).ToLowerInvariant();
	}

	private static string GenerateToken()
	{
		return new string(Enumerable.Range(0, 32)
			.Select(_ => TokenAlphabet[Random.Shared.Next(TokenAlphabet.Length)]).ToArray());
	}
}
