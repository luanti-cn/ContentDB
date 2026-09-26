// ContentDB C# —— 组队房间服务
// 队长建房得 6 位邀请码 → 好友凭码进房 → 队长设目标服务器 → 成员客户端轮询状态一键跟随。

using ContentDB.Core.Abstractions;
using ContentDB.Core.Domain;
using ContentDB.Core.Services;
using ContentDB.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContentDB.Infrastructure.Services;

public sealed class PartyService : IPartyService
{
	private const string CodeAlphabet = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";
	private const int MaxMembers = 20;

	private readonly AppDbContext _db;
	private readonly IRealtimeHub _hub;

	public PartyService(AppDbContext db, IRealtimeHub hub)
	{
		_db = db;
		_hub = hub;
	}

	public async Task<ServiceResult> CreateAsync(User actor, string? serverAddress, CancellationToken ct = default)
	{
		var existing = await FindActivePartyIdAsync(actor.Id, ct);
		if (existing is not null)
			return ServiceResult.Fail(409, "Already in a party; leave first");

		string? address = null;
		if (!string.IsNullOrWhiteSpace(serverAddress))
		{
			address = ServerAddress.Normalize(serverAddress);
			if (address is null) return ServiceResult.Fail(400, "Invalid server address");
		}

		var party = new Party
		{
			Code = await GenerateCodeAsync(ct),
			LeaderId = actor.Id,
			ServerAddress = address,
		};
		party.Members.Add(new PartyMember { UserId = actor.Id });
		_db.Parties.Add(party);
		await _db.SaveChangesAsync(ct);

		var state = await BuildStateAsync(party, ct);
		await PushAsync([actor.Id], state, ct);
		return ServiceResult.Ok(state);
	}

	public async Task<ServiceResult> JoinAsync(User actor, string code, CancellationToken ct = default)
	{
		var normalized = (code ?? "").Replace("-", "").Replace(" ", "").ToUpperInvariant();
		var party = await _db.Parties
			.FirstOrDefaultAsync(p => p.Code == normalized && p.Status == PartyStatus.ACTIVE, ct);
		if (party is null) return ServiceResult.Fail(404, "Party not found or already ended");

		var mine = await FindActivePartyIdAsync(actor.Id, ct);
		if (mine == party.Id)
			return ServiceResult.Ok(await BuildStateAsync(party, ct)); // 幂等
		if (mine is not null)
			return ServiceResult.Fail(409, "Already in another party; leave first");

		var memberCount = await _db.PartyMembers.CountAsync(m => m.PartyId == party.Id, ct);
		if (memberCount >= MaxMembers)
			return ServiceResult.Fail(409, $"Party is full ({MaxMembers})");

		_db.PartyMembers.Add(new PartyMember { PartyId = party.Id, UserId = actor.Id });
		await _db.SaveChangesAsync(ct);

		var state = await BuildStateAsync(party, ct);
		await PushAsync(party.Members.Select(m => m.UserId).ToList(), state, ct);
		return ServiceResult.Ok(state);
	}

	public async Task<ServiceResult> LeaveAsync(User actor, CancellationToken ct = default)
	{
		var (party, myMember) = await FindMembershipAsync(actor.Id, ct);
		if (party is null || myMember is null) return ServiceResult.Fail(404, "Not in a party");

		var memberIds = party.Members.Select(m => m.UserId).ToList();
		if (party.LeaderId == actor.Id)
		{
			// 队长退出 => 解散
			party.Status = PartyStatus.ENDED;
			party.EndedAt = DateTimeOffset.UtcNow;
			_db.PartyMembers.RemoveRange(party.Members);
		}
		else
		{
			_db.PartyMembers.Remove(myMember);
		}
		await _db.SaveChangesAsync(ct);
		await PushAsync(memberIds, null, ct);
		return ServiceResult.Ok(new { success = true });
	}

	public async Task<ServiceResult> EndAsync(User actor, CancellationToken ct = default)
	{
		var party = await _db.Parties
			.Include(p => p.Members)
			.FirstOrDefaultAsync(p => p.LeaderId == actor.Id && p.Status == PartyStatus.ACTIVE, ct);
		if (party is null) return ServiceResult.Fail(404, "No active party");

		var memberIds = party.Members.Select(m => m.UserId).ToList();
		party.Status = PartyStatus.ENDED;
		party.EndedAt = DateTimeOffset.UtcNow;
		_db.PartyMembers.RemoveRange(party.Members);
		await _db.SaveChangesAsync(ct);
		await PushAsync(memberIds, null, ct);
		return ServiceResult.Ok(new { success = true });
	}

	public async Task<ServiceResult> SetServerAsync(User actor, string address, CancellationToken ct = default)
	{
		var party = await _db.Parties
			.FirstOrDefaultAsync(p => p.LeaderId == actor.Id && p.Status == PartyStatus.ACTIVE, ct);
		if (party is null) return ServiceResult.Fail(404, "No active party (leader only)");

		var normalized = ServerAddress.Normalize(address);
		if (normalized is null) return ServiceResult.Fail(400, "Invalid server address");
		party.ServerAddress = normalized;
		await _db.SaveChangesAsync(ct);

		var state = await BuildStateAsync(party, ct);
		await PushAsync(party.Members.Select(m => m.UserId).ToList(), state, ct);
		return ServiceResult.Ok(state);
	}

	public async Task<ServiceResult> KickAsync(User actor, string username, CancellationToken ct = default)
	{
		var party = await _db.Parties
			.Include(p => p.Members).ThenInclude(m => m.User)
			.FirstOrDefaultAsync(p => p.LeaderId == actor.Id && p.Status == PartyStatus.ACTIVE, ct);
		if (party is null) return ServiceResult.Fail(404, "No active party (leader only)");

		var target = party.Members.FirstOrDefault(m => m.User.Username == username);
		if (target is null) return ServiceResult.Fail(404, "Member not found");
		if (target.UserId == actor.Id) return ServiceResult.Fail(400, "Cannot kick yourself");

		var kickedId = target.UserId;
		_db.PartyMembers.Remove(target);
		await _db.SaveChangesAsync(ct);

		await PushAsync([kickedId], null, ct);
		var state = await BuildStateAsync(party, ct);
		await PushAsync(party.Members.Select(m => m.UserId).ToList(), state, ct);
		return ServiceResult.Ok(new { success = true });
	}

	public async Task<PartyState?> GetStateAsync(User actor, CancellationToken ct = default)
	{
		var partyId = await FindActivePartyIdAsync(actor.Id, ct);
		if (partyId is null) return null;

		var party = await _db.Parties
			.Include(p => p.Members).ThenInclude(m => m.User)
			.FirstAsync(p => p.Id == partyId, ct);
		return await BuildStateAsync(party, ct);
	}

	// ---- 内部 ----

	/// <summary>把组队状态变更推给相关用户(在线连接);state=null 表示「已不在房间」。</summary>
	private Task PushAsync(IReadOnlyCollection<int> userIds, PartyState? state, CancellationToken ct)
		=> _hub.SendToUsersAsync(userIds, new { type = "party.update", party = state }, ct);

	private Task<int?> FindActivePartyIdAsync(int userId, CancellationToken ct)
		=> _db.PartyMembers
			.Where(m => m.UserId == userId && m.Party!.Status == PartyStatus.ACTIVE)
			.Select(m => (int?)m.PartyId)
			.FirstOrDefaultAsync(ct);

	private async Task<(Party? Party, PartyMember? Mine)> FindMembershipAsync(int userId, CancellationToken ct)
	{
		var party = await _db.Parties
			.Include(p => p.Members)
			.FirstOrDefaultAsync(p => p.Status == PartyStatus.ACTIVE && p.Members.Any(m => m.UserId == userId), ct);
		return (party, party?.Members.FirstOrDefault(m => m.UserId == userId));
	}

	private async Task<PartyState> BuildStateAsync(Party party, CancellationToken ct)
	{
		await _db.Entry(party).Collection(p => p.Members).Query().Include(m => m.User).LoadAsync();
		if (party.Leader == null!)
			await _db.Entry(party).Reference(p => p.Leader).LoadAsync();

		var ids = party.Members.Select(m => m.UserId).ToList();
		var presence = await PresenceLookup.LookupAsync(_db, ids, ct);

		var members = party.Members
			.OrderBy(m => m.UserId == party.LeaderId ? 0 : 1)
			.ThenBy(m => m.JoinedAt)
			.Select(m =>
			{
				var has = presence.TryGetValue(m.UserId, out var p);
				return new PartyMemberInfo(
					m.User.Username, m.User.DisplayName,
					IsLeader: m.UserId == party.LeaderId,
					Online: has,
					CurrentServerAddress: has ? p.Server : null);
			})
			.ToList();

		return new PartyState(party.Id, party.Code, party.Leader!.Username, party.ServerAddress, party.CreatedAt, members);
	}

	private async Task<string> GenerateCodeAsync(CancellationToken ct)
	{
		for (var attempt = 0; attempt < 5; attempt++)
		{
			var code = new string(Enumerable.Range(0, 6)
				.Select(_ => CodeAlphabet[Random.Shared.Next(CodeAlphabet.Length)]).ToArray());
			if (!await _db.Parties.AnyAsync(p => p.Code == code && p.Status == PartyStatus.ACTIVE, ct))
				return code;
		}
		throw new InvalidOperationException("无法生成唯一邀请码,请重试");
	}
}
