// ContentDB C# —— 联机房间(Host 开本地游戏登记态)
// 房间是纯内存态:进程重启即空,Host 心跳续期,60s 无心跳自动过期。
// 加入需与 Host 为好友;返回 Host 候选(打洞/中继用),后续信令走 WS 透传。

using System.Collections.Concurrent;
using System.Security.Cryptography;
using ContentDB.Core.Abstractions;
using ContentDB.Core.Domain;
using ContentDB.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ContentDB.Infrastructure.Services;

/// <summary>房间内存表(单例)。</summary>
public sealed class HostRoomStore
{
	public sealed class HostRoom
	{
		public required string RoomId { get; init; }
		public required string RoomCode { get; init; }
		public required int HostUserId { get; init; }
		public required string HostUsername { get; init; }
		public volatile string? Status;
		public volatile string? CandidatesJson;

		/// <summary>中继分配(Host 视角,含票据;加入方仅见 host/port)。</summary>
		public volatile RelayInfo? Relay;

		/// <summary>该房间所在的中继节点名(横向扩展路由;null = 无中继)。</summary>
		public volatile string? RelayNode;

		public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

		// volatile 不支持 DateTimeOffset,用 ticks + Interlocked
		private long _lastSeenTicks = DateTimeOffset.UtcNow.UtcTicks;
		public DateTimeOffset LastSeenAt => new(Interlocked.Read(ref _lastSeenTicks), TimeSpan.Zero);
		public void Touch() => Interlocked.Exchange(ref _lastSeenTicks, DateTimeOffset.UtcNow.UtcTicks);
	}

	private const int RoomTtlSeconds = 60;

	private readonly ConcurrentDictionary<string, HostRoom> _rooms = new();

	public HostRoom? FindByHost(int hostUserId)
	{
		Sweep();
		return _rooms.Values.FirstOrDefault(r => r.HostUserId == hostUserId && !IsExpired(r));
	}

	public HostRoom? FindByCode(string roomCode)
	{
		Sweep();
		var compact = roomCode.Replace("-", "").Replace(" ", "").ToUpperInvariant();
		return _rooms.Values.FirstOrDefault(r => r.RoomCode.Replace("-", "") == compact && !IsExpired(r));
	}

	public HostRoom? FindByRoomId(string roomId)
	{
		Sweep();
		return _rooms.TryGetValue(roomId, out var room) && !IsExpired(room) ? room : null;
	}

	public HostRoom Add(HostRoom room)
	{
		_rooms[room.RoomId] = room;
		return room;
	}

	public bool Remove(string roomId) => _rooms.TryRemove(roomId, out _);

	public static bool IsExpired(HostRoom room)
		=> DateTimeOffset.UtcNow - room.LastSeenAt > TimeSpan.FromSeconds(RoomTtlSeconds);

	/// <summary>惰性清扫过期房间。</summary>
	private void Sweep()
	{
		foreach (var (id, room) in _rooms)
			if (IsExpired(room)) _rooms.TryRemove(id, out _);
	}
}

public sealed class HostRoomService : IHostRoomService
{
	private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

	private readonly HostRoomStore _store;
	private readonly AppDbContext _db;
	private readonly IRealtimeHub _hub;
	private readonly IRelayClient _relay;
	private readonly ILogger<HostRoomService> _logger;

	public HostRoomService(HostRoomStore store, AppDbContext db, IRealtimeHub hub, IRelayClient relay,
		ILogger<HostRoomService> logger)
	{
		_store = store;
		_db = db;
		_hub = hub;
		_relay = relay;
		_logger = logger;
	}

	public async Task<ServiceResult> RegisterAsync(User actor, string? status, string? candidatesJson, CancellationToken ct = default)
	{
		var room = _store.FindByHost(actor.Id);
		if (room is null)
		{
			room = _store.Add(new HostRoomStore.HostRoom
			{
				RoomId = Guid.NewGuid().ToString("N"),
				RoomCode = GenerateCode(),
				HostUserId = actor.Id,
				HostUsername = actor.Username,
			});
		}
		UpdateRoom(room, status, candidatesJson);
		await EnsureRelayAsync(room, ct);
		return ServiceResult.Ok(ToInfo(room));
	}

	public async Task<ServiceResult> HeartbeatAsync(User actor, string? status, string? candidatesJson, CancellationToken ct = default)
	{
		var room = _store.FindByHost(actor.Id);
		if (room is null) return ServiceResult.Fail(404, "No active room; register first");

		var candidatesChanged = candidatesJson is not null && candidatesJson != room.CandidatesJson;
		UpdateRoom(room, status, candidatesJson);
		await EnsureRelayAsync(room, ct);

		// 候选变化 → 推给房间内已有的加入方
		if (candidatesChanged)
			_ = _hub.SendToRoomAsync(room.RoomId, new
			{
				type = "room.candidates",
				roomId = room.RoomId,
				candidates = ParseJson(room.CandidatesJson),
				status = room.Status,
			});

		return ServiceResult.Ok(ToInfo(room));
	}

	public Task<ServiceResult> CloseAsync(User actor, CancellationToken ct = default)
	{
		var room = _store.FindByHost(actor.Id);
		if (room is null) return Task.FromResult(ServiceResult.Fail(404, "No active room"));
		_store.Remove(room.RoomId);
		if (room.RelayNode is not null)
			_ = _relay.DeallocateAsync(room.RelayNode, room.RoomId, ct); // 尽力而为;Relay 也会按空闲回收
		return Task.FromResult(ServiceResult.Ok(new { success = true }));
	}

	public async Task<ServiceResult> JoinByCodeAsync(User actor, string roomCode, CancellationToken ct = default)
	{
		if (string.IsNullOrWhiteSpace(roomCode)) return ServiceResult.Fail(400, "roomCode is required");
		var room = _store.FindByCode(roomCode);
		if (room is null) return ServiceResult.Fail(404, "Room not found or expired");
		return await JoinRoomAsync(actor, room, ct);
	}

	public async Task<ServiceResult> JoinByUserAsync(User actor, string hostUsername, CancellationToken ct = default)
	{
		var host = await _db.Users.FirstOrDefaultAsync(u => u.Username == (hostUsername ?? "").Trim(), ct);
		if (host is null) return ServiceResult.Fail(404, "User not found");
		var room = _store.FindByHost(host.Id);
		if (room is null) return ServiceResult.Fail(404, $"{host.Username} is not hosting right now");
		return await JoinRoomAsync(actor, room, ct);
	}

	private async Task<ServiceResult> JoinRoomAsync(User actor, HostRoomStore.HostRoom room, CancellationToken ct)
	{
		if (actor.Id == room.HostUserId) return ServiceResult.Fail(400, "Host cannot join own room");

		var isFriend = await _db.FriendLinks.AnyAsync(l =>
			l.Status == FriendLinkStatus.ACCEPTED
			&& ((l.RequesterId == actor.Id && l.AddresseeId == room.HostUserId)
				|| (l.RequesterId == room.HostUserId && l.AddresseeId == actor.Id)), ct);
		if (!isFriend) return ServiceResult.Fail(403, "Only friends can join");

		await EnsureRelayAsync(room, ct);

		// 加入方只拿 host/port,不带 Host 注册票据
		var relayForGuest = room.Relay is { } r ? new RelayInfo(r.Host, r.Port, null) : null;
		return ServiceResult.Ok(new HostRoomJoin(
			room.RoomId, room.HostUsername, room.Status, room.CandidatesJson, relayForGuest));
	}

	/// <summary>确保房间已有中继分配(幂等;失败降级纯 P2P)。
	/// 多节点:轮询选节点,失败换下一个;房间记住所在节点,join/close 路由到同一节点。</summary>
	private async Task EnsureRelayAsync(HostRoomStore.HostRoom room, CancellationToken ct)
	{
		if (room.Relay is not null) return;

		var nodes = _relay.Nodes;
		if (nodes.Count == 0) return;

		// 已有节点记忆(上次分配失败前的节点)则优先重试它
		string? preferred = room.RelayNode;
		var order = preferred is not null
			? nodes.Where(n => n.Name == preferred).Concat(nodes.Where(n => n.Name != preferred)).ToList()
			: nodes.OrderBy(_ => Interlocked.Increment(ref _rrCounter) % nodes.Count).ToList();

		foreach (var node in order)
		{
			var allocation = await _relay.AllocateAsync(node.Name, room.RoomId, ct);
			if (allocation is null) continue;

			room.RelayNode = node.Name;
			room.Relay = new RelayInfo(node.PublicHost, allocation.Port, allocation.Ticket);
			return;
		}
		_logger.LogWarning("所有中继节点不可用({Count} 个),房间 {RoomId} 降级纯 P2P", nodes.Count, room.RoomId);
	}

	private static void UpdateRoom(HostRoomStore.HostRoom room, string? status, string? candidatesJson)
	{
		if (status is not null) room.Status = status;
		if (candidatesJson is not null) room.CandidatesJson = candidatesJson;
		room.Touch();
	}

	private long _rrCounter;

	private HostRoomInfo ToInfo(HostRoomStore.HostRoom room)
		=> new(room.RoomId, room.RoomCode, room.HostUsername, room.Status,
			room.CandidatesJson, room.CreatedAt, room.Relay);

	private static object? ParseJson(string? json)
		=> json is null ? null : System.Text.Json.JsonSerializer.Deserialize<object>(json);

	private static string GenerateCode()
	{
		var raw = new string(Enumerable.Range(0, 8)
			.Select(_ => CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)])
			.ToArray());
		return raw.Insert(4, "-");
	}
}
