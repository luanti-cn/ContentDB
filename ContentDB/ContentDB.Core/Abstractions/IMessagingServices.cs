// ContentDB C# —— 实时通讯抽象:好友私聊 / WebSocket 推送中枢 / 联机房间(打洞信令)

using ContentDB.Core.Domain;

namespace ContentDB.Core.Abstractions;

public sealed record ChatMessageInfo(
	long Id, string From, string? FromDisplay, string To, string Body,
	DateTimeOffset CreatedAt, DateTimeOffset? ReadAt);

public sealed record UnreadPeerInfo(string Username, string? DisplayName, int Count, DateTimeOffset? LastAt);

public sealed record HistoryPage(IReadOnlyList<ChatMessageInfo> Items, bool HasMore);

/// <summary>好友私聊(REST 历史与 WS 实时共用同一服务)。</summary>
public interface IDirectMessageService
{
	/// <summary>发送私聊(仅好友;含频率限制)。落库并向接收方在线连接推送 dm.new。</summary>
	Task<ServiceResult> SendAsync(User sender, string recipientUsername, string body, CancellationToken ct = default);

	/// <summary>与某好友的历史(倒序游标分页:beforeId 为空取最新一页)。</summary>
	Task<HistoryPage> HistoryAsync(User actor, string peerUsername, long? beforeId, int limit, CancellationToken ct = default);

	/// <summary>按好友分组的未读计数(含最近一条时间)。</summary>
	Task<IReadOnlyList<UnreadPeerInfo>> UnreadAsync(User actor, CancellationToken ct = default);

	/// <summary>标记来自某好友的消息全部已读,返回标记条数(0 表示无未读)。</summary>
	Task<int> MarkReadAsync(User actor, string peerUsername, CancellationToken ct = default);
}

/// <summary>
/// 实时推送中枢(单例)。维护 userId → WebSocket 连接集合,
/// 供各服务(私聊/组队/好友/房间信令)向在线用户推送 JSON 消息。
/// </summary>
public interface IRealtimeHub
{
	/// <summary>该用户当前是否至少有一条在线连接。</summary>
	bool IsOnline(int userId);

	/// <summary>向用户全部在线连接推送(序列化后发送;无连接时静默忽略)。</summary>
	Task SendToUserAsync(int userId, object payload, CancellationToken ct = default);

	/// <summary>向多个用户推送。</summary>
	Task SendToUsersAsync(IEnumerable<int> userIds, object payload, CancellationToken ct = default);

	/// <summary>向房间内全部成员推送(可排除某人)。</summary>
	Task SendToRoomAsync(string roomId, object payload, int? exceptUserId = null, CancellationToken ct = default);

	/// <summary>向指定用户位于某房间内的连接推送。</summary>
	Task SendToUserInRoomAsync(int userId, string roomId, object payload, CancellationToken ct = default);
}

/// <summary>中继候选(Host/加入方共用;Ticket 仅 Host 持有)。</summary>
public sealed record RelayInfo(string Host, int Port, string? Ticket);

/// <summary>中继分配(主后端 → Relay 内部 API)。</summary>
public sealed record RelayAllocation(string RoomId, int Port, string Ticket);

/// <summary>中继节点(横向扩展:房间分片到具体节点;客户端只见节点的 PublicHost)。</summary>
public sealed record RelayNodeInfo(string Name, string PublicHost);

/// <summary>
/// 中继客户端;失败返回 null(降级纯 P2P,不阻塞房间创建)。
/// 多节点:node 为节点名,由调用方(HostRoomService)选节点并随房间记忆路由。
/// </summary>
public interface IRelayClient
{
	/// <summary>可用节点列表(空 = 中继未启用)。</summary>
	IReadOnlyList<RelayNodeInfo> Nodes { get; }

	/// <summary>在指定节点为房间分配中继端口(幂等;返回 null 表示该节点不可用)。</summary>
	Task<RelayAllocation?> AllocateAsync(string node, string roomId, CancellationToken ct = default);

	/// <summary>在指定节点释放房间端口(尽力而为)。</summary>
	Task DeallocateAsync(string node, string roomId, CancellationToken ct = default);
}

/// <summary>联机房间(Host 开本地游戏后的登记态;内存态,进程重启即空)。</summary>
public sealed record HostRoomInfo(
	string RoomId, string RoomCode, string HostUsername, string? Status,
	string? CandidatesJson, DateTimeOffset CreatedAt, RelayInfo? Relay);

/// <summary>加入方视角:房间信息 + Host 当前候选(供打洞/中继连接;无 Host 票据)。</summary>
public sealed record HostRoomJoin(
	string RoomId, string HostUsername, string? Status,
	string? CandidatesJson, RelayInfo? Relay);

/// <summary>Host 开服登记与信令(内存表,无持久化)。</summary>
public interface IHostRoomService
{
	/// <summary>开服登记(重复调用刷新);TTL 由心跳维持,断连 60s 后过期。</summary>
	Task<ServiceResult> RegisterAsync(User actor, string? status, string? candidatesJson, CancellationToken ct = default);

	/// <summary>心跳续期(可同时更新状态/候选)。</summary>
	Task<ServiceResult> HeartbeatAsync(User actor, string? status, string? candidatesJson, CancellationToken ct = default);

	Task<ServiceResult> CloseAsync(User actor, CancellationToken ct = default);

	/// <summary>按房间码加入(需与 Host 为好友)。</summary>
	Task<ServiceResult> JoinByCodeAsync(User actor, string roomCode, CancellationToken ct = default);

	/// <summary>按 Host 用户名加入(好友面板「加入」按钮)。</summary>
	Task<ServiceResult> JoinByUserAsync(User actor, string hostUsername, CancellationToken ct = default);
}
