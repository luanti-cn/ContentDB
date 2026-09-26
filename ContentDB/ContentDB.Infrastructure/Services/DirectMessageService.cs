// ContentDB C# —— 好友私聊服务实现
// 发送校验:好友关系 + 未被拉黑 + 长度/频率限制;落库后向接收方在线连接推送 dm.new。

using System.Collections.Concurrent;
using ContentDB.Core.Abstractions;
using ContentDB.Core.Domain;
using ContentDB.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContentDB.Infrastructure.Services;

/// <summary>简易滑动窗口限流(单例):每用户每分钟 20 条。</summary>
public sealed class MessageRateLimiter
{
	private readonly ConcurrentDictionary<int, Queue<DateTimeOffset>> _hits = [];
	private readonly TimeSpan _window = TimeSpan.FromMinutes(1);
	private readonly int _limit = 20;

	public bool Allow(int userId)
	{
		var now = DateTimeOffset.UtcNow;
		var q = _hits.GetOrAdd(userId, _ => new Queue<DateTimeOffset>());
		lock (q)
		{
			while (q.Count > 0 && now - q.Peek() > _window) q.Dequeue();
			if (q.Count >= _limit) return false;
			q.Enqueue(now);
			return true;
		}
	}
}

public sealed class DirectMessageService : IDirectMessageService
{
	public const int MaxBodyLength = 2000;

	private readonly AppDbContext _db;
	private readonly IRealtimeHub _hub;
	private readonly MessageRateLimiter _rate;

	public DirectMessageService(AppDbContext db, IRealtimeHub hub, MessageRateLimiter rate)
	{
		_db = db;
		_hub = hub;
		_rate = rate;
	}

	public async Task<ServiceResult> SendAsync(User sender, string recipientUsername, string body, CancellationToken ct = default)
	{
		recipientUsername = (recipientUsername ?? "").Trim();
		body = (body ?? "").Trim();
		if (body.Length == 0) return ServiceResult.Fail(400, "Message is empty");
		if (body.Length > MaxBodyLength) return ServiceResult.Fail(400, $"Message too long (max {MaxBodyLength})");
		if (!_rate.Allow(sender.Id)) return ServiceResult.Fail(429, "Sending too fast, slow down");

		var recipient = await _db.Users.FirstOrDefaultAsync(u => u.Username == recipientUsername, ct);
		if (recipient is null || !recipient.IsActive) return ServiceResult.Fail(404, "User not found");
		if (recipient.Id == sender.Id) return ServiceResult.Fail(400, "Cannot message yourself");

		var link = await _db.FriendLinks.FirstOrDefaultAsync(l =>
			(l.RequesterId == sender.Id && l.AddresseeId == recipient.Id)
			|| (l.RequesterId == recipient.Id && l.AddresseeId == sender.Id), ct);
		if (link is null || link.Status != FriendLinkStatus.ACCEPTED)
			return ServiceResult.Fail(403, "You can only message friends");

		var message = new DirectMessage
		{
			SenderId = sender.Id,
			RecipientId = recipient.Id,
			Body = body,
		};
		_db.DirectMessages.Add(message);
		await _db.SaveChangesAsync(ct);

		// 推送给接收方全部在线连接(离线时靠 REST 未读数兜底)
		await _hub.SendToUserAsync(recipient.Id, new
		{
			type = "dm.new",
			from = sender.Username,
			fromDisplay = sender.DisplayName,
			body,
			ts = message.CreatedAt,
		}, ct);

		return ServiceResult.Ok(new ChatMessageInfo(
			message.Id, sender.Username, sender.DisplayName,
			recipient.Username, body, message.CreatedAt, null));
	}

	public async Task<HistoryPage> HistoryAsync(User actor, string peerUsername, long? beforeId, int limit, CancellationToken ct = default)
	{
		peerUsername = (peerUsername ?? "").Trim();
		limit = Math.Clamp(limit, 1, 100);

		var peer = await _db.Users.FirstOrDefaultAsync(u => u.Username == peerUsername, ct);
		if (peer is null) return new HistoryPage([], false);

		var query = _db.DirectMessages
			.Include(m => m.Sender)
			.Where(m => (m.SenderId == actor.Id && m.RecipientId == peer.Id)
				|| (m.SenderId == peer.Id && m.RecipientId == actor.Id));
		if (beforeId is { } before)
			query = query.Where(m => m.Id < before);

		var items = await query
			.OrderByDescending(m => m.Id)
			.Take(limit + 1)
			.ToListAsync(ct);

		var hasMore = items.Count > limit;
		if (hasMore) items = items.Take(limit).ToList();

		return new HistoryPage(items.Select(m => new ChatMessageInfo(
			m.Id,
			m.SenderId == actor.Id ? actor.Username : peer.Username,
			m.SenderId == actor.Id ? actor.DisplayName : peer.DisplayName,
			m.SenderId == actor.Id ? peer.Username : actor.Username,
			m.Body, m.CreatedAt, m.ReadAt)).ToList(), hasMore);
	}

	public async Task<IReadOnlyList<UnreadPeerInfo>> UnreadAsync(User actor, CancellationToken ct = default)
	{
		var rows = await _db.DirectMessages
			.Include(m => m.Sender)
			.Where(m => m.RecipientId == actor.Id && m.ReadAt == null)
			.GroupBy(m => new { m.SenderId, m.Sender.Username, m.Sender.DisplayName })
			.Select(g => new
			{
				g.Key.Username,
				g.Key.DisplayName,
				Count = g.Count(),
				LastAt = g.Max(m => m.CreatedAt),
			})
			.ToListAsync(ct);

		return rows.Select(r => new UnreadPeerInfo(r.Username, r.DisplayName, r.Count, r.LastAt)).ToList();
	}

	public async Task<int> MarkReadAsync(User actor, string peerUsername, CancellationToken ct = default)
	{
		peerUsername = (peerUsername ?? "").Trim();
		var peer = await _db.Users.FirstOrDefaultAsync(u => u.Username == peerUsername, ct);
		if (peer is null) return 0;

		var unread = await _db.DirectMessages
			.Where(m => m.RecipientId == actor.Id && m.SenderId == peer.Id && m.ReadAt == null)
			.ToListAsync(ct);
		if (unread.Count == 0) return 0;

		var now = DateTimeOffset.UtcNow;
		foreach (var m in unread) m.ReadAt = now;
		await _db.SaveChangesAsync(ct);

		// 已读回执推给对方在线连接
		await _hub.SendToUserAsync(peer.Id, new { type = "dm.read", by = actor.Username, ts = now }, ct);
		return unread.Count;
	}
}
