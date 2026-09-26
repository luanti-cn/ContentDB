// ContentDB C# —— 实时通讯 WebSocket 网关
// /api/cloud/client/ws/(device token 或网页会话认证)
//
// 协议(一帧一 JSON 对象,详见 docs/multiplayer.md §3.2):
//   客户端→服务端:ping / dm.send / dm.read / presence / room.host / room.signal / room.join
//   服务端→客户端:hello / pong / dm.new / dm.ack / dm.read / presence /
//                  friend.request / party.update / room.candidates / room.signal / error
//
// 认证顺序:Authorization: Bearer <device_token> → 网页 Cookie 会话。

using System.Net.WebSockets;
using System.Text.Json;
using ContentDB.Api.Auth;
using ContentDB.Core.Abstractions;
using ContentDB.Core.Domain;
using ContentDB.Core.Services;
using ContentDB.Infrastructure.Data;
using ContentDB.Infrastructure.Realtime;
using ContentDB.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace ContentDB.Api.Controllers;

[ApiController]
public sealed class RealtimeController : ApiControllerBase
{
	private const int MaxFrameBytes = 32 * 1024;

	private readonly RealtimeHub _hub;
	private readonly HostRoomStore _rooms;
	private readonly WsTicketStore _tickets;
	private readonly IServiceScopeFactory _scopes;

	public RealtimeController(ICurrentUserAccessor currentUser, RealtimeHub hub,
		HostRoomStore rooms, WsTicketStore tickets, IServiceScopeFactory scopes) : base(currentUser)
	{
		_hub = hub;
		_rooms = rooms;
		_tickets = tickets;
		_scopes = scopes;
	}

	/// <summary>换取 WS 连接票据(60 秒一次性;网页跨域场景用,避免 cookie 随 WS 握手发送的问题)。</summary>
	[HttpPost("/api/cloud/client/ws-ticket/")]
	public async Task<IActionResult> WsTicket()
	{
		var (user, deviceId, error) = await ResolveUserAsync();
		if (error is not null) return ApiError(error.Value, "Authentication needed");
		var ticket = _tickets.Issue(user!.Id, deviceId);
		return Ok(new { ticket, expiresIn = 60 });
	}

	[HttpGet("/api/cloud/client/ws/")]
	public async Task WebSocket()
	{
		var (user, deviceId, authError) = await ResolveUserAsync();
		if (authError is not null)
		{
			Response.StatusCode = authError.Value;
			return;
		}
		if (!HttpContext.WebSockets.IsWebSocketRequest)
		{
			Response.StatusCode = 400;
			return;
		}

		using var socket = await HttpContext.WebSockets.AcceptWebSocketAsync();
		var session = new Session(user!, deviceId, socket, _hub, _rooms, _scopes, HttpContext.RequestAborted);
		await session.RunAsync();
	}

	/// <summary>
	/// 认证顺序:一次性票据(?ticket=,网页跨域场景)→ device token(Bearer)→ 网页 Cookie 会话。
	/// 返回(用户, 设备Id, 错误状态码)。
	/// </summary>
	private async Task<(User? user, int? deviceId, int? error)> ResolveUserAsync()
	{
		// 0) 一次性票据(换取时已认证;此处仅验票取身份)
		var viaTicket = _tickets.TryTake(Request.Query["ticket"]);
		if (viaTicket is not null)
		{
			using var scope0 = _scopes.CreateScope();
			var db0 = scope0.ServiceProvider.GetRequiredService<AppDbContext>();
			var user0 = await db0.Users.FirstOrDefaultAsync(u => u.Id == viaTicket.Value.UserId, HttpContext.RequestAborted);
			if (user0 is null || !user0.IsActive || user0.IsBanned) return (null, null, 403);
			return (user0, viaTicket.Value.DeviceId, null);
		}

		var dev = await HttpContext.AuthenticateAsync(AuthenticationSetup.DeviceTokenScheme);
		if (dev.Succeeded
			&& HttpContext.Items[DeviceTokenAuthenticationHandler.DeviceItemKey] is PairedDevice device)
		{
			if (!device.IsActive || device.User is not { IsActive: true } || device.User.IsBanned)
				return (null, null, 403);
			return (device.User, device.Id, null);
		}

		var user = await GetUserAsync();
		if (user is null) return (null, null, 401);
		if (user.IsBanned) return (null, null, 403);
		return (user, null, null);
	}

	// ---- 会话:读循环 + 消息分发 ----

	private sealed class Session
	{
		private readonly User _user;
		private readonly int? _deviceId;
		private readonly WebSocket _socket;
		private readonly RealtimeHub _hub;
		private readonly HostRoomStore _rooms;
		private readonly IServiceScopeFactory _scopes;
		private readonly CancellationToken _ct;

		public Session(User user, int? deviceId, WebSocket socket, RealtimeHub hub,
			HostRoomStore rooms, IServiceScopeFactory scopes, CancellationToken ct)
		{
			_user = user;
			_deviceId = deviceId;
			_socket = socket;
			_hub = hub;
			_rooms = rooms;
			_scopes = scopes;
			_ct = ct;
		}

		public async Task RunAsync()
		{
			var connection = new RealtimeConnection
			{
				Socket = _socket,
				UserId = _user.Id,
				DeviceId = _deviceId,
			};
			var unregister = _hub.Register(connection);
			try
			{
				await SendAsync(new
				{
					type = "hello",
					user = new { username = _user.Username, displayName = _user.DisplayName },
					deviceId = _deviceId,
				});
				await BroadcastPresenceToFriendsAsync(online: true);

				var buffer = new byte[MaxFrameBytes];
				var message = new MemoryStream();
				while (!_socket.CloseStatus.HasValue && !_ct.IsCancellationRequested)
				{
					message.SetLength(0);
					WebSocketReceiveResult result;
					do
					{
						result = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), _ct);
						if (result.MessageType == WebSocketMessageType.Close)
							return;
						if (message.Length + result.Count > MaxFrameBytes)
							return; // 超长帧直接断开
						message.Write(buffer, 0, result.Count);
					}
					while (!result.EndOfMessage);

					if (message.Length == 0) continue;
					await HandleMessageAsync(connection, message.ToArray());
				}
			}
			catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException)
			{
				// 正常断开/网络中断
			}
			finally
			{
				unregister();
				try
				{
					// 离线广播用 CancellationToken.None:连接断开后 RequestAborted 已取消
					await BroadcastPresenceToFriendsAsync(online: false, ct: CancellationToken.None);
				}
				catch
				{
					// 尽力而为
				}
			}
		}

		private async Task HandleMessageAsync(RealtimeConnection connection, byte[] bytes)
		{
			JsonElement json;
			try
			{
				using var doc = JsonDocument.Parse(bytes);
				json = doc.RootElement.Clone();
			}
			catch (JsonException)
			{
				await SendAsync(new { type = "error", code = 400, message = "Invalid JSON" });
				return;
			}

			var type = json.TryGetProperty("type", out var t) ? t.GetString() : null;
			switch (type)
			{
				case "ping":
					await SendAsync(new { type = "pong" });
					break;

				case "dm.send":
					await HandleDmSendAsync(json);
					break;

				case "dm.read":
					await HandleDmReadAsync(json);
					break;

				case "presence":
					await HandlePresenceAsync(json);
					break;

				case "room.host":
					await HandleRoomHostAsync(connection, json);
					break;

				case "room.join":
					await HandleRoomJoinAsync(connection, json);
					break;

				case "room.signal":
					await HandleRoomSignalAsync(connection, json);
					break;

				default:
					await SendAsync(new { type = "error", code = 400, message = $"Unknown type: {type}" });
					break;
			}
		}

		private async Task HandleDmSendAsync(JsonElement json)
		{
			var to = json.TryGetProperty("to", out var toEl) ? toEl.GetString() : null;
			var body = json.TryGetProperty("body", out var bodyEl) ? bodyEl.GetString() : null;
			var clientId = json.TryGetProperty("clientId", out var cidEl) ? cidEl.GetString() : null;

			using var scope = _scopes.CreateScope();
			var dm = scope.ServiceProvider.GetRequiredService<IDirectMessageService>();
			var result = await dm.SendAsync(_user, to ?? "", body ?? "", _ct);
			if (!result.Success)
			{
				await SendAsync(new { type = "error", code = result.StatusCode, message = result.Error, refClientId = clientId });
				return;
			}
			if (result.Payload is ChatMessageInfo info)
				await _hub.SendToUserAsync(_user.Id, new
				{
					type = "dm.ack",
					clientId,
					id = info.Id,
					to = info.To,
					ts = info.CreatedAt,
				}, _ct);
		}

		private async Task HandleDmReadAsync(JsonElement json)
		{
			var from = json.TryGetProperty("from", out var fromEl) ? fromEl.GetString() : null;
			if (string.IsNullOrEmpty(from)) return;
			using var scope = _scopes.CreateScope();
			var dm = scope.ServiceProvider.GetRequiredService<IDirectMessageService>();
			await dm.MarkReadAsync(_user, from!, _ct);
		}

		private async Task HandlePresenceAsync(JsonElement json)
		{
			var address = json.TryGetProperty("address", out var addrEl) && addrEl.ValueKind == JsonValueKind.String
				? addrEl.GetString() : null;
			if (_deviceId is null) return; // 网页会话无设备,不落 presence
			using var scope = _scopes.CreateScope();
			var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
			var device = await db.PairedDevices
				.Include(d => d.User)
				.FirstOrDefaultAsync(d => d.Id == _deviceId, _ct);
			if (device is null) return;

			await DevicePairingPresenceAsync(db, device, address);
		}

		/// <summary>更新设备游戏 presence 并向好友广播 inGame 事件(与 REST 心跳同一套语义)。</summary>
		private async Task DevicePairingPresenceAsync(AppDbContext db, PairedDevice device, string? address)
		{
			var wasOnline = device.PresenceUpdatedAt is not null
				&& DateTimeOffset.UtcNow - device.PresenceUpdatedAt.Value < PresenceLookup.OnlineWindow;
			var previousAddress = device.CurrentServerAddress;

			device.CurrentServerAddress = ServerAddress.Normalize(address);
			device.PresenceUpdatedAt = DateTimeOffset.UtcNow;
			await db.SaveChangesAsync(_ct);

			var wasGameOnline = wasOnline && previousAddress is not null;
			var isGameOnline = device.CurrentServerAddress is not null;
			var addressChanged = device.CurrentServerAddress != previousAddress;
			if (isGameOnline != wasGameOnline || (isGameOnline && addressChanged))
			{
				var friendIds = await db.FriendLinks
					.Where(l => l.Status == FriendLinkStatus.ACCEPTED
						&& (l.RequesterId == _user.Id || l.AddresseeId == _user.Id))
					.Select(l => l.RequesterId == _user.Id ? l.AddresseeId : l.RequesterId)
					.ToListAsync(_ct);
				await _hub.SendToUsersAsync(friendIds, new
				{
					type = "presence",
					username = _user.Username,
					online = isGameOnline,
					inGame = true,
					address = device.CurrentServerAddress,
				}, _ct);
			}
		}

		private async Task HandleRoomHostAsync(RealtimeConnection connection, JsonElement json)
		{
			var roomId = json.TryGetProperty("roomId", out var ridEl) ? ridEl.GetString() : null;
			var status = json.TryGetProperty("status", out var stEl) && stEl.ValueKind == JsonValueKind.String
				? stEl.GetString() : null;
			var candidates = json.TryGetProperty("candidates", out var candEl)
				? candEl.GetRawText() : null;
			if (string.IsNullOrEmpty(roomId)) return;

			using var scope = _scopes.CreateScope();
			var rooms = scope.ServiceProvider.GetRequiredService<IHostRoomService>();

			// 心跳续期;房间不存在(过期/重启)则自动重建
			var result = await rooms.HeartbeatAsync(_user, status, candidates, _ct);
			if (!result.Success && result.StatusCode == 404)
				result = await rooms.RegisterAsync(_user, status, candidates, _ct);
			if (!result.Success)
			{
				await SendAsync(new { type = "error", code = result.StatusCode, message = result.Error });
				return;
			}

			connection.RoomId = roomId;
			_hub.MarkRoomOnline(roomId);
		}

		private async Task HandleRoomJoinAsync(RealtimeConnection connection, JsonElement json)
		{
			var roomId = json.TryGetProperty("roomId", out var ridEl) ? ridEl.GetString() : null;
			if (string.IsNullOrEmpty(roomId)) return;

			var room = _rooms.FindByRoomId(roomId!);
			if (room is null)
			{
				await SendAsync(new { type = "error", code = 404, message = "Room not found or expired" });
				return;
			}
			if (room.HostUserId == _user.Id)
			{
				await SendAsync(new { type = "error", code = 400, message = "Host cannot join own room" });
				return;
			}

			// 仅好友可进入房间信令通道
			using var scope = _scopes.CreateScope();
			var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
			var isFriend = await db.FriendLinks.AnyAsync(l =>
				l.Status == FriendLinkStatus.ACCEPTED
				&& ((l.RequesterId == _user.Id && l.AddresseeId == room.HostUserId)
					|| (l.RequesterId == room.HostUserId && l.AddresseeId == _user.Id)), _ct);
			if (!isFriend)
			{
				await SendAsync(new { type = "error", code = 403, message = "Only friends can join" });
				return;
			}

			connection.RoomId = roomId;
			await SendAsync(new
			{
				type = "room.candidates",
				roomId,
				candidates = ParseJson(room.CandidatesJson),
				status = room.Status,
				host = room.HostUsername,
				// 加入方只拿 host/port,不带 Host 注册票据
				relay = room.Relay is { } relay ? new { host = relay.Host, port = relay.Port } : null,
			});
		}

		private async Task HandleRoomSignalAsync(RealtimeConnection connection, JsonElement json)
		{
			var roomId = json.TryGetProperty("roomId", out var ridEl) ? ridEl.GetString() : null;
			var to = json.TryGetProperty("to", out var toEl) ? toEl.GetString() : null;
			if (string.IsNullOrEmpty(roomId) || string.IsNullOrEmpty(to)) return;

			// 只能给自己所在房间的其他成员发信令
			if (connection.RoomId != roomId)
			{
				await SendAsync(new { type = "error", code = 403, message = "Not in this room" });
				return;
			}

			using var scope = _scopes.CreateScope();
			var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
			var target = await db.Users.FirstOrDefaultAsync(u => u.Username == to, _ct);
			if (target is null) return;

			await _hub.SendToUserAsync(target.Id, new
			{
				type = "room.signal",
				roomId,
				from = _user.Username,
				payload = json.TryGetProperty("payload", out var p) ? p.Clone() : (JsonElement?)null,
			}, _ct);
		}

		/// <summary>向所有好友广播我的网站(WS)上下线。inGame=false:仅表示站点在线,不代表在游戏里。</summary>
		private async Task BroadcastPresenceToFriendsAsync(bool online, string? address = null, CancellationToken ct = default)
		{
			ct = ct == default ? _ct : ct;
			using var scope = _scopes.CreateScope();
			var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
			var friendIds = await db.FriendLinks
				.Where(l => l.Status == FriendLinkStatus.ACCEPTED
					&& (l.RequesterId == _user.Id || l.AddresseeId == _user.Id))
				.Select(l => l.RequesterId == _user.Id ? l.AddresseeId : l.RequesterId)
				.ToListAsync(ct);
			if (friendIds.Count == 0) return;

			await _hub.SendToUsersAsync(friendIds, new
			{
				type = "presence",
				username = _user.Username,
				online,
				inGame = false,
				address = (string?)null,
			}, ct);
		}

		private async Task SendAsync(object payload)
		{
			var text = JsonSerializer.Serialize(payload, JsonOptions);
			var bytes = System.Text.Encoding.UTF8.GetBytes(text);
			await _socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text,
				endOfMessage: true, _ct);
		}

		private static object? ParseJson(string? json)
			=> json is null ? null : JsonSerializer.Deserialize<object>(json);

		private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
	}
}
