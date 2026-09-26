// ContentDB.Relay —— UDP 中继核心
// 一个房间 = 一个 UDP 端口。Host 持票据注册后,数据面规则:
//   Host 端点发包 → 扇出给全部活跃 Guest 端点;
//   未知端点(即 Guest)发包 → 记为 Guest 并转发给 Host 端点。
// 注册包:ASCII "LRCN1|{roomId}|{ticket}"(Host 首包);其余包原样转发,内容不解析。

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace ContentDB.Relay;

public sealed record AllocationResult(string RoomId, int Port, string PublicAddress, string Ticket);

public sealed record RoomStatus(
	string RoomId, int Port, bool HostRegistered, int Guests,
	DateTimeOffset CreatedAt, DateTimeOffset LastActivity);

public sealed class RelayServer : IDisposable
{
	private sealed class Session
	{
		public required string RoomId { get; init; }
		public required int Port { get; init; }
		public required string Ticket { get; init; }
		public required UdpClient Client { get; init; }
		public required SemaphoreSlim SendLock { get; init; }
		public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
		public volatile IPEndPoint? HostEndpoint;

		// 活跃 Guest:端点 → 最后活动 ticks
		public readonly ConcurrentDictionary<string, GuestEntry> Guests = [];

		// 限速:滑动窗口计数
		private long _windowStartTicks = DateTimeOffset.UtcNow.UtcTicks;
		private long _windowBytes;

		public DateTimeOffset LastActivity
		{
			get
			{
				var latest = CreatedAt.UtcTicks;
				foreach (var g in Guests.Values)
					latest = Math.Max(latest, Interlocked.Read(ref g.LastSeenTicks));
				var host = Interlocked.Read(ref _lastHostActivityTicks);
				return new(Math.Max(latest, host == 0 ? latest : host), TimeSpan.Zero);
			}
		}

		private long _lastHostActivityTicks;

		public void TouchHost()
			=> Interlocked.Exchange(ref _lastHostActivityTicks, DateTimeOffset.UtcNow.UtcTicks);

		/// <summary>限速检查(粗粒度 1s 窗口);超速返回 false(丢包)。</summary>
		public bool TryConsume(int bytes, int limitPerSecond)
		{
			var now = DateTimeOffset.UtcNow.UtcTicks;
			lock (this)
			{
				if (now - _windowStartTicks >= TimeSpan.TicksPerSecond)
				{
					_windowStartTicks = now;
					_windowBytes = 0;
				}
				if (_windowBytes + bytes > limitPerSecond) return false;
				_windowBytes += bytes;
				return true;
			}
		}
	}

	public sealed class GuestEntry
	{
		public required IPEndPoint Endpoint { get; init; }
		public long LastSeenTicks = DateTimeOffset.UtcNow.UtcTicks;
	}

	private readonly RelayOptions _options;
	private readonly ILogger<RelayServer> _logger;
	private readonly object _lock = new();
	private readonly Dictionary<string, Session> _sessions = []; // roomId → session
	private readonly HashSet<int> _usedPorts = [];
	private readonly CancellationTokenSource _cts = new();

	public RelayServer(IOptions<RelayOptions> options, ILogger<RelayServer> logger)
	{
		_options = options.Value;
		_logger = logger;
	}

	// ---- 控制面 ----

	public AllocationResult? Allocate(string roomId)
	{
		if (string.IsNullOrEmpty(_options.InternalSecret)) return null; // 未配置密钥 = 禁用

		lock (_lock)
		{
			if (_sessions.TryGetValue(roomId, out var existing))
				return ToResult(existing); // 幂等:同房间复用

			if (_sessions.Count >= _options.MaxRooms)
			{
				_logger.LogWarning("房间数已达上限 {Max},拒绝分配 {RoomId}", _options.MaxRooms, roomId);
				return null;
			}

			var port = PickPort();
			if (port is null) return null;

			var client = new UdpClient();
			try
			{
				client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, false);
				// 空配置 => IPv4 全网卡(IPv6Any 与默认 IPv4 socket 不兼容,会报 10047)
				var bind = string.IsNullOrEmpty(_options.UdpBindAddress)
					? IPAddress.Any
					: IPAddress.Parse(_options.UdpBindAddress);
				client.Client.Bind(new IPEndPoint(bind, port.Value));
			}
			catch (Exception ex)
			{
				client.Dispose();
				_usedPorts.Remove(port.Value); // 已持锁(Allocate 内),直接归还
				_logger.LogWarning(ex, "UDP 端口 {Port} 绑定失败", port);
				return null;
			}

			var session = new Session
			{
				RoomId = roomId,
				Port = port.Value,
				Ticket = GenerateTicket(),
				Client = client,
				SendLock = new SemaphoreSlim(1, 1),
			};
			_sessions[roomId] = session;
			_usedPorts.Add(port.Value);

			_ = Task.Run(() => ReceiveLoopAsync(session, _cts.Token));
			_logger.LogInformation("房间 {RoomId} 分配端口 {Port}", roomId, port);
			return ToResult(session);
		}
	}

	public bool Deallocate(string roomId)
	{
		Session? session;
		lock (_lock)
		{
			if (!_sessions.Remove(roomId, out session)) return false;
			_usedPorts.Remove(session.Port);
		}
		try { session.Client.Close(); } catch { /* 已关闭 */ }
		_logger.LogInformation("房间 {RoomId} 回收端口 {Port}", roomId, session.Port);
		return true;
	}

	public IReadOnlyList<RoomStatus> ListRooms()
	{
		lock (_lock)
		{
			return _sessions.Values
				.Select(s => new RoomStatus(s.RoomId, s.Port, s.HostEndpoint is not null, s.Guests.Count,
					s.CreatedAt, s.LastActivity))
				.ToList();
		}
	}

	/// <summary>空闲回收:已注册但无活动 / 未注册超宽限的房间。</summary>
	public void ReapIdle()
	{
		List<Session> expired = [];
		lock (_lock)
		{
			foreach (var s in _sessions.Values)
			{
				if (s.HostEndpoint is null)
				{
					if (DateTimeOffset.UtcNow - s.CreatedAt > TimeSpan.FromSeconds(_options.RegisterGraceSeconds))
						expired.Add(s);
				}
				else if (DateTimeOffset.UtcNow - s.LastActivity > TimeSpan.FromSeconds(_options.RoomIdleSeconds))
				{
					expired.Add(s);
				}
			}
		}
		foreach (var s in expired)
		{
			Deallocate(s.RoomId);
			_logger.LogInformation("房间 {RoomId} 空闲回收", s.RoomId);
		}
	}

	// ---- 数据面 ----

	private async Task ReceiveLoopAsync(Session session, CancellationToken ct)
	{
		var buffer = new byte[64 * 1024];
		var registrationPrefix = Encoding.ASCII.GetBytes($"LRCN1|{session.RoomId}|");
		while (!ct.IsCancellationRequested)
		{
			IPEndPoint remote = new(IPAddress.Any, 0);
			int len;
			try
			{
				var data = await session.Client.ReceiveAsync(ct);
				len = data.Buffer.Length;
				if (len == 0) continue;
				remote = data.RemoteEndPoint;
				Buffer.BlockCopy(data.Buffer, 0, buffer, 0, Math.Min(len, buffer.Length));
			}
			catch (Exception) when (ct.IsCancellationRequested || session.Client.Client is not { IsBound: true })
			{
				return; // 正常关闭
			}
			catch (SocketException)
			{
				continue; // ICMP 瞬时错误,继续收
			}
			catch (ObjectDisposedException)
			{
				return;
			}

			// 1) Host 注册包
			if (len > registrationPrefix.Length + 8
				&& buffer.AsSpan(0, registrationPrefix.Length).SequenceEqual(registrationPrefix))
			{
				var ticket = Encoding.ASCII.GetString(buffer, registrationPrefix.Length, len - registrationPrefix.Length);
				if (FixedTimeEquals(ticket, session.Ticket))
				{
					session.HostEndpoint = remote;
					session.TouchHost();
					_logger.LogInformation("房间 {RoomId} Host 注册 {Ep}", session.RoomId, remote);
				}
				continue;
			}

			// 2) Host 数据 → 扇出 Guest
			if (SameEndpoint(remote, session.HostEndpoint))
			{
				session.TouchHost();
				if (!session.TryConsume(len, _options.RateLimitBytesPerSecond)) continue;

				foreach (var guest in ActiveGuests(session))
					await SendAsync(session, buffer, len, guest);
				continue;
			}

			// 3) Guest 数据 → 记录端点并转发 Host
			var host = session.HostEndpoint;
			if (host is null) continue; // Host 未注册前不转发
			if (!session.TryConsume(len, _options.RateLimitBytesPerSecond)) continue;

			var key = remote.ToString();
			session.Guests[key] = new GuestEntry { Endpoint = remote };
			PruneIdleGuests(session);

			if (session.Guests.Count <= _options.MaxGuestsPerRoom)
				await SendAsync(session, buffer, len, host);
		}
	}

	private async Task SendAsync(Session session, byte[] buffer, int len, IPEndPoint target)
	{
		try
		{
			await session.SendLock.WaitAsync();
			try
			{
				await session.Client.SendAsync(buffer.AsMemory(0, len), target);
			}
			finally
			{
				session.SendLock.Release();
			}
		}
		catch (Exception ex) when (ex is SocketException or ObjectDisposedException or OperationCanceledException)
		{
			_logger.LogDebug(ex, "房间 {RoomId} 发送到 {Ep} 失败", session.RoomId, target);
		}
	}

	private IReadOnlyList<IPEndPoint> ActiveGuests(Session session)
	{
		var cutoff = DateTimeOffset.UtcNow.UtcTicks - TimeSpan.FromSeconds(_options.GuestIdleSeconds).Ticks;
		return session.Guests.Values
			.Where(g => Interlocked.Read(ref g.LastSeenTicks) > cutoff)
			.Select(g => g.Endpoint)
			.ToList();
	}

	private void PruneIdleGuests(Session session)
	{
		if (session.Guests.Count <= _options.MaxGuestsPerRoom) return;
		var cutoff = DateTimeOffset.UtcNow.UtcTicks - TimeSpan.FromSeconds(_options.GuestIdleSeconds).Ticks;
		foreach (var (key, g) in session.Guests)
			if (Interlocked.Read(ref g.LastSeenTicks) <= cutoff)
				session.Guests.TryRemove(key, out _);
	}

	// ---- 内部 ----

	private int? PickPort()
	{
		var span = _options.UdpPortEnd - _options.UdpPortStart;
		if (span <= 0) return null;
		for (var attempt = 0; attempt < 20; attempt++)
		{
			var port = _options.UdpPortStart + RandomNumberGenerator.GetInt32(span);
			if (_usedPorts.Add(port)) return port;
		}
		return null;
	}

	private AllocationResult ToResult(Session s)
		=> new(s.RoomId, s.Port, _options.PublicAddress, s.Ticket);

	private static string GenerateTicket()
	{
		Span<byte> bytes = stackalloc byte[24];
		RandomNumberGenerator.Fill(bytes);
		return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
	}

	private static bool SameEndpoint(IPEndPoint a, IPEndPoint? b)
		=> b is not null && a.Port == b.Port && a.Address.Equals(b.Address);

	private static bool FixedTimeEquals(string a, string b)
	{
		var ba = Encoding.ASCII.GetBytes(a);
		var bb = Encoding.ASCII.GetBytes(b);
		return CryptographicOperations.FixedTimeEquals(ba, bb);
	}

	public void Dispose()
	{
		_cts.Cancel();
		lock (_lock)
		{
			foreach (var s in _sessions.Values)
				try { s.Client.Dispose(); } catch { }
			_sessions.Clear();
			_usedPorts.Clear();
		}
		_cts.Dispose();
	}
}
