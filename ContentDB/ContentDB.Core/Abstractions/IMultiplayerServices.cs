// ContentDB C# —— 联机服务抽象:游戏服务器大厅 / 组队房间 / 服务器列表回源

using ContentDB.Core.Domain;

namespace ContentDB.Core.Abstractions;

public sealed record GameServerInfo(
	int Id, string Address, string Name, string? Description, string? WebsiteUrl,
	string Owner, int PlayersOnline, int PlayersMax, bool Online, bool Verified,
	int OurPlayersOnline, DateTimeOffset? ReportedAt);

public sealed record MyGameServerInfo(
	int Id, string Address, string Name, string? Description, string? WebsiteUrl,
	int PlayersOnline, int PlayersMax, bool Online, bool Listed, bool Verified,
	string? ReportTokenPrefix, DateTimeOffset CreatedAt);

public sealed record AdminGameServerInfo(
	int Id, string Address, string Name, string Owner, bool Listed, bool Verified,
	int PlayersOnline, int PlayersMax, bool Online, DateTimeOffset CreatedAt);

public sealed record ServerReportToken(int Id, string Address, string ReportToken);

public sealed record PartyMemberInfo(
	string Username, string? DisplayName, bool IsLeader, bool Online, string? CurrentServerAddress);

public sealed record PartyState(
	int Id, string Code, string Leader, string? ServerAddress, DateTimeOffset CreatedAt,
	IReadOnlyList<PartyMemberInfo> Members);

public interface IGameServerService
{
	/// <summary>公开服务器列表(在线优先),含本站用户在线数。</summary>
	Task<IReadOnlyList<GameServerInfo>> ListAsync(CancellationToken ct = default);

	Task<IReadOnlyList<MyGameServerInfo>> ListMineAsync(User owner, CancellationToken ct = default);

	Task<ServiceResult> RegisterAsync(User actor, string address, string name,
		string? description, string? websiteUrl, CancellationToken ct = default);

	Task<ServiceResult> UpdateAsync(User actor, int id, string? name, string? description,
		string? websiteUrl, bool? listed, CancellationToken ct = default);

	Task<ServiceResult> DeleteAsync(User actor, int id, CancellationToken ct = default);

	/// <summary>重新生成服务器状态上报 token(明文仅返回一次)。</summary>
	Task<ServiceResult> RegenerateReportTokenAsync(User actor, int id, CancellationToken ct = default);

	/// <summary>服务器端 mod 定期上报实时状态(token 鉴权)。</summary>
	Task<ServiceResult> ReportStatusAsync(string address, string token,
		int playersOnline, int playersMax, string? motd, CancellationToken ct = default);

	/// <summary>管理员:全量服务器列表(含未上架)。</summary>
	Task<IReadOnlyList<AdminGameServerInfo>> AdminListAsync(CancellationToken ct = default);

	/// <summary>管理员:设置认证标记 / 上架状态。</summary>
	Task<ServiceResult> AdminSetFlagsAsync(User actor, int id, bool? verified, bool? listed,
		CancellationToken ct = default);
}

public interface IPartyService
{
	Task<PartyState?> GetStateAsync(User actor, CancellationToken ct = default);

	Task<ServiceResult> CreateAsync(User actor, string? serverAddress, CancellationToken ct = default);

	Task<ServiceResult> JoinAsync(User actor, string code, CancellationToken ct = default);

	Task<ServiceResult> LeaveAsync(User actor, CancellationToken ct = default);

	Task<ServiceResult> EndAsync(User actor, CancellationToken ct = default);

	Task<ServiceResult> SetServerAsync(User actor, string address, CancellationToken ct = default);

	Task<ServiceResult> KickAsync(User actor, string username, CancellationToken ct = default);
}
