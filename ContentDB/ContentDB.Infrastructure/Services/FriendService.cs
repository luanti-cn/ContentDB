// ContentDB C# —— 好友服务实现
// 关系单行存储(Requester/Addressee),查询好友时取双向;在线状态来自配对设备心跳。

using ContentDB.Core.Abstractions;
using ContentDB.Core.Domain;
using ContentDB.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContentDB.Infrastructure.Services;

public sealed class FriendService : IFriendService
{
	private readonly AppDbContext _db;
	private readonly INotificationService _notifications;
	private readonly IRealtimeHub _hub;

	public FriendService(AppDbContext db, INotificationService notifications, IRealtimeHub hub)
	{
		_db = db;
		_notifications = notifications;
		_hub = hub;
	}

	public async Task<IReadOnlyList<FriendInfo>> ListFriendsAsync(User owner, CancellationToken ct = default)
	{
		var links = await _db.FriendLinks
			.Include(l => l.Requester)
			.Include(l => l.Addressee)
			.Where(l => (l.RequesterId == owner.Id || l.AddresseeId == owner.Id)
				&& l.Status == FriendLinkStatus.ACCEPTED)
			.OrderByDescending(l => l.RespondedAt ?? l.CreatedAt)
			.ToListAsync(ct);
		if (links.Count == 0) return [];

		// 在线状态:各好友所有设备中最近一次心跳(5 分钟内视为在线)
		var ids = links
			.Select(l => l.RequesterId == owner.Id ? l.AddresseeId : l.RequesterId)
			.ToList();
		var latest = await PresenceLookup.LookupAsync(_db, ids, ct);

		return links
			.Select(l =>
			{
				var friend = l.RequesterId == owner.Id ? l.Addressee : l.Requester;
				var presence = latest.TryGetValue(friend.Id, out var p) ? p : default;
				return new FriendInfo(
					friend.Username, friend.DisplayName, friend.ProfilePicUrl,
					l.RespondedAt ?? l.CreatedAt,
					presence.At,
					presence.Server,
					SiteOnline: _hub.IsOnline(friend.Id));
			})
			.ToList();
	}

	public async Task<(IReadOnlyList<FriendRequestInfo> Incoming, IReadOnlyList<FriendRequestInfo> Outgoing)> ListRequestsAsync(User owner, CancellationToken ct = default)
	{
		var pending = await _db.FriendLinks
			.Include(l => l.Requester)
			.Include(l => l.Addressee)
			.Where(l => l.Status == FriendLinkStatus.PENDING
				&& (l.RequesterId == owner.Id || l.AddresseeId == owner.Id))
			.ToListAsync(ct);

		var incoming = pending
			.Where(l => l.AddresseeId == owner.Id)
			.Select(l => new FriendRequestInfo(l.Id, l.Requester.Username, l.Requester.DisplayName))
			.ToList();
		var outgoing = pending
			.Where(l => l.RequesterId == owner.Id)
			.Select(l => new FriendRequestInfo(l.Id, l.Addressee.Username, l.Addressee.DisplayName))
			.ToList();
		return (incoming, outgoing);
	}

	public async Task<ServiceResult> SendRequestAsync(User actor, string targetUsername, CancellationToken ct = default)
	{
		targetUsername = (targetUsername ?? "").Trim();
		var target = await _db.Users.FirstOrDefaultAsync(u => u.Username == targetUsername, ct);
		if (target is null || !target.IsActive)
			return ServiceResult.Fail(404, "User not found");
		if (target.Id == actor.Id)
			return ServiceResult.Fail(400, "Cannot add yourself");

		var link = await FindLinkAsync(actor.Id, target.Id, ct);
		if (link is not null)
		{
			switch (link.Status)
			{
				case FriendLinkStatus.ACCEPTED:
					return ServiceResult.Fail(409, "Already friends");
				case FriendLinkStatus.BLOCKED:
					return ServiceResult.Fail(403, "Request not allowed");
				case FriendLinkStatus.PENDING when link.RequesterId == actor.Id:
					return ServiceResult.Fail(409, "Request already sent");
				case FriendLinkStatus.PENDING:
					// 对方已先申请我 => 直接成为好友
					link.Status = FriendLinkStatus.ACCEPTED;
					link.RespondedAt = DateTimeOffset.UtcNow;
					await _db.SaveChangesAsync(ct);
					await _notifications.NotifyAsync(
						link.RequesterId, actor.Id, NotificationType.FRIEND_REQUEST,
						$"{actor.Username} 接受了你的好友申请", $"/users/{actor.Username}", ct: ct);
					await _hub.SendToUserAsync(link.RequesterId, new
					{
						type = "friend.accepted",
						username = actor.Username,
						displayName = actor.DisplayName,
					}, ct);
					return ServiceResult.Ok(new { success = true, accepted = true });
			}
		}

		var request = new FriendLink
		{
			RequesterId = actor.Id,
			AddresseeId = target.Id,
			Status = FriendLinkStatus.PENDING,
		};
		_db.FriendLinks.Add(request);
		await _db.SaveChangesAsync(ct);

		await _notifications.NotifyAsync(
			target.Id, actor.Id, NotificationType.FRIEND_REQUEST,
			$"{actor.Username} 请求加你为好友", "/friends", ct: ct);
		await _hub.SendToUserAsync(target.Id, new
		{
			type = "friend.request",
			from = actor.Username,
			fromDisplay = actor.DisplayName,
		}, ct);
		return ServiceResult.Ok(new { success = true });
	}

	public async Task<ServiceResult> AcceptAsync(User actor, int requestId, CancellationToken ct = default)
	{
		var link = await _db.FriendLinks
			.FirstOrDefaultAsync(l => l.Id == requestId && l.AddresseeId == actor.Id && l.Status == FriendLinkStatus.PENDING, ct);
		if (link is null) return ServiceResult.Fail(404, "Request not found");

		link.Status = FriendLinkStatus.ACCEPTED;
		link.RespondedAt = DateTimeOffset.UtcNow;
		await _db.SaveChangesAsync(ct);

		await _notifications.NotifyAsync(
			link.RequesterId, actor.Id, NotificationType.FRIEND_REQUEST,
			$"{actor.Username} 接受了你的好友申请", $"/users/{actor.Username}", ct: ct);
		await _hub.SendToUserAsync(link.RequesterId, new
		{
			type = "friend.accepted",
			username = actor.Username,
			displayName = actor.DisplayName,
		}, ct);
		return ServiceResult.Ok(new { success = true });
	}

	public async Task<ServiceResult> RejectOrWithdrawAsync(User actor, int requestId, CancellationToken ct = default)
	{
		var link = await _db.FriendLinks
			.FirstOrDefaultAsync(l => l.Id == requestId && l.Status == FriendLinkStatus.PENDING
				&& (l.RequesterId == actor.Id || l.AddresseeId == actor.Id), ct);
		if (link is null) return ServiceResult.Fail(404, "Request not found");

		_db.FriendLinks.Remove(link);
		await _db.SaveChangesAsync(ct);
		return ServiceResult.Ok(new { success = true });
	}

	public async Task<ServiceResult> RemoveFriendAsync(User actor, string friendUsername, CancellationToken ct = default)
	{
		var target = await _db.Users.FirstOrDefaultAsync(u => u.Username == friendUsername, ct);
		if (target is null) return ServiceResult.Fail(404, "User not found");

		var link = await _db.FriendLinks
			.FirstOrDefaultAsync(l => l.Status == FriendLinkStatus.ACCEPTED
				&& ((l.RequesterId == actor.Id && l.AddresseeId == target.Id)
					|| (l.RequesterId == target.Id && l.AddresseeId == actor.Id)), ct);
		if (link is null) return ServiceResult.Fail(404, "Not friends");

		_db.FriendLinks.Remove(link);
		await _db.SaveChangesAsync(ct);
		return ServiceResult.Ok(new { success = true });
	}

	public async Task<ServiceResult> BlockAsync(User actor, string username, CancellationToken ct = default)
	{
		var target = await _db.Users.FirstOrDefaultAsync(u => u.Username == username, ct);
		if (target is null) return ServiceResult.Fail(404, "User not found");
		if (target.Id == actor.Id) return ServiceResult.Fail(400, "Cannot block yourself");

		var link = await FindLinkAsync(actor.Id, target.Id, ct);
		if (link is not null)
		{
			if (link.Status == FriendLinkStatus.BLOCKED && link.RequesterId == actor.Id)
				return ServiceResult.Ok(new { success = true }); // 已拉黑

			// 方向不符或存在好友/申请关系:删除旧记录,重建为"我拉黑对方"
			_db.FriendLinks.Remove(link);
			await _db.SaveChangesAsync(ct);
		}

		_db.FriendLinks.Add(new FriendLink
		{
			RequesterId = actor.Id,
			AddresseeId = target.Id,
			Status = FriendLinkStatus.BLOCKED,
		});
		await _db.SaveChangesAsync(ct);
		return ServiceResult.Ok(new { success = true });
	}

	public async Task<ServiceResult> UnblockAsync(User actor, string username, CancellationToken ct = default)
	{
		var target = await _db.Users.FirstOrDefaultAsync(u => u.Username == username, ct);
		if (target is null) return ServiceResult.Fail(404, "User not found");

		var link = await _db.FriendLinks
			.FirstOrDefaultAsync(l => l.Status == FriendLinkStatus.BLOCKED
				&& l.RequesterId == actor.Id && l.AddresseeId == target.Id, ct);
		if (link is null) return ServiceResult.Fail(404, "Not blocked");

		_db.FriendLinks.Remove(link);
		await _db.SaveChangesAsync(ct);
		return ServiceResult.Ok(new { success = true });
	}

	/// <summary>取两用户间的关系记录(不区分方向)。</summary>
	private Task<FriendLink?> FindLinkAsync(int userA, int userB, CancellationToken ct)
		=> _db.FriendLinks.FirstOrDefaultAsync(l =>
			(l.RequesterId == userA && l.AddresseeId == userB)
			|| (l.RequesterId == userB && l.AddresseeId == userA), ct);
}
