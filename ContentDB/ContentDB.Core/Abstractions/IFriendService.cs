// ContentDB C# —— 好友服务抽象

using ContentDB.Core.Domain;

namespace ContentDB.Core.Abstractions;

/// <summary>
/// 好友(含双态在线:SiteOnline = 实时连接在线(网页/客户端);Online = 游戏在线(服务器心跳,5 分钟窗口)。
/// </summary>
public sealed record FriendInfo(
	string Username, string? DisplayName, string? ProfilePicUrl,
	DateTimeOffset FriendsSince,
	DateTimeOffset? PresenceAt, string? CurrentServerAddress,
	bool SiteOnline = false)
{
	public bool Online => PresenceAt is not null
		&& PresenceAt.Value > DateTimeOffset.UtcNow - TimeSpan.FromMinutes(5);
}

/// <summary>好友申请(id 用于接受/拒绝)。</summary>
public sealed record FriendRequestInfo(int Id, string Username, string? DisplayName);

public interface IFriendService
{
	Task<IReadOnlyList<FriendInfo>> ListFriendsAsync(User owner, CancellationToken ct = default);

	/// <summary>返回(收到的申请, 发出的申请)。</summary>
	Task<(IReadOnlyList<FriendRequestInfo> Incoming, IReadOnlyList<FriendRequestInfo> Outgoing)> ListRequestsAsync(User owner, CancellationToken ct = default);

	/// <summary>发申请;若对方已先申请我则直接成为好友。</summary>
	Task<ServiceResult> SendRequestAsync(User actor, string targetUsername, CancellationToken ct = default);

	Task<ServiceResult> AcceptAsync(User actor, int requestId, CancellationToken ct = default);

	/// <summary>拒绝收到的申请 / 撤回发出的申请(删除记录,可再次申请)。</summary>
	Task<ServiceResult> RejectOrWithdrawAsync(User actor, int requestId, CancellationToken ct = default);

	Task<ServiceResult> RemoveFriendAsync(User actor, string friendUsername, CancellationToken ct = default);

	/// <summary>拉黑:对方无法给我发申请;双向均不可见为好友。</summary>
	Task<ServiceResult> BlockAsync(User actor, string username, CancellationToken ct = default);

	Task<ServiceResult> UnblockAsync(User actor, string username, CancellationToken ct = default);
}
