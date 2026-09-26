// ContentDB C# —— 实时推送中枢实现
// userId → 连接集合(支持同用户多端:网页 + launcher 并存)。
// 发送失败/已关闭的连接自动摘除。线程安全:全部操作持锁。

using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;
using ContentDB.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace ContentDB.Infrastructure.Realtime;

/// <summary>一条在线 WS 连接(带元数据,供房间/设备维度过滤扩展)。</summary>
public sealed class RealtimeConnection
{
	public required WebSocket Socket { get; init; }
	public required int UserId { get; init; }
	public int? DeviceId { get; init; }

	/// <summary>连接加入的房间 id(Host 或加入方;null = 不在任何房间)。</summary>
	public volatile string? RoomId;

	private volatile bool _closed;
	public bool IsClosed => _closed || Socket.CloseStatus.HasValue;

	public void MarkClosed() => _closed = true;
}

public sealed class RealtimeHub : IRealtimeHub
{
	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

	private readonly object _lock = new();
	private readonly Dictionary<int, List<RealtimeConnection>> _byUser = [];
	private readonly ConcurrentDictionary<string, byte> _onlineRooms = [];

	private readonly ILogger<RealtimeHub> _logger;

	public RealtimeHub(ILogger<RealtimeHub> logger) => _logger = logger;

	public bool IsOnline(int userId)
	{
		lock (_lock)
		{
			return _byUser.TryGetValue(userId, out var list) && list.Any(c => !c.IsClosed);
		}
	}

	/// <summary>注册连接;返回移除动作(断开时调用)。</summary>
	public Action Register(RealtimeConnection connection)
	{
		lock (_lock)
		{
			if (!_byUser.TryGetValue(connection.UserId, out var list))
				_byUser[connection.UserId] = list = [];
			list.Add(connection);
		}
		return () =>
		{
			connection.MarkClosed();
			bool empty;
			lock (_lock)
			{
				if (_byUser.TryGetValue(connection.UserId, out var list2))
				{
					list2.RemoveAll(c => ReferenceEquals(c, connection) || c.IsClosed);
					empty = list2.Count == 0;
					if (empty) _byUser.Remove(connection.UserId);
				}
				else empty = true;
			}
			if (connection.RoomId is not null) _onlineRooms.TryRemove(connection.RoomId, out _);
		};
	}

	/// <summary>房间 id 在线标记(Host 房间存在性的内存事实源之一)。</summary>
	public void MarkRoomOnline(string roomId) => _onlineRooms[roomId] = 1;
	public bool IsRoomOnline(string roomId) => _onlineRooms.ContainsKey(roomId);

	/// <summary>向用户全部在线连接广播。</summary>
	public async Task SendToUserAsync(int userId, object payload, CancellationToken ct = default)
	{
		RealtimeConnection[] targets;
		lock (_lock)
		{
			if (!_byUser.TryGetValue(userId, out var list)) return;
			targets = [.. list.Where(c => !c.IsClosed)];
		}
		if (targets.Length == 0) return;

		var text = JsonSerializer.Serialize(payload, JsonOptions);
		foreach (var conn in targets)
			await SendTextAsync(conn, text, ct);
	}

	public async Task SendToUsersAsync(IEnumerable<int> userIds, object payload, CancellationToken ct = default)
	{
		foreach (var uid in userIds.Distinct())
			await SendToUserAsync(uid, payload, ct);
	}

	/// <summary>向房间内全部成员推送(可排除某人;Host 与加入方都靠 RoomId 归组)。</summary>
	public async Task SendToRoomAsync(string roomId, object payload, int? exceptUserId = null, CancellationToken ct = default)
	{
		RealtimeConnection[] targets;
		lock (_lock)
		{
			targets = _byUser.Values.SelectMany(v => v)
				.Where(c => !c.IsClosed && c.RoomId == roomId
					&& (exceptUserId is null || c.UserId != exceptUserId))
				.ToArray();
		}
		if (targets.Length == 0) return;

		var text = JsonSerializer.Serialize(payload, JsonOptions);
		foreach (var conn in targets)
			await SendTextAsync(conn, text, ct);
	}

	/// <summary>向指定用户的连接里 RoomId 匹配的那部分推送(信令回传给发起方的其他端)。</summary>
	public async Task SendToUserInRoomAsync(int userId, string roomId, object payload, CancellationToken ct = default)
	{
		RealtimeConnection[] targets;
		lock (_lock)
		{
			if (!_byUser.TryGetValue(userId, out var list)) return;
			targets = [.. list.Where(c => !c.IsClosed && c.RoomId == roomId)];
		}
		if (targets.Length == 0) return;

		var text = JsonSerializer.Serialize(payload, JsonOptions);
		foreach (var conn in targets)
			await SendTextAsync(conn, text, ct);
	}

	private async Task SendTextAsync(RealtimeConnection conn, string text, CancellationToken ct)
	{
		try
		{
			if (conn.IsClosed) return;
			var bytes = System.Text.Encoding.UTF8.GetBytes(text);
			await conn.Socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text,
				endOfMessage: true, ct);
		}
		catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException)
		{
			_logger.LogDebug(ex, "WS 发送失败,连接将被摘除 userId={UserId}", conn.UserId);
			conn.MarkClosed();
		}
	}
}
