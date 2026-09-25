// ContentDB C# —— 好友关系
// 单行建模一条关系,Requester = 发起方,Addressee = 接收方;
// PENDING(待处理)-> ACCEPTED;BLOCKED 单向拉黑(Requester 拉黑 Addressee)。

namespace ContentDB.Core.Domain;

public enum FriendLinkStatus
{
	PENDING,
	ACCEPTED,
	BLOCKED,
}

public class FriendLink
{
	public int Id { get; set; }

	public int RequesterId { get; set; }
	public User Requester { get; set; } = null!;

	public int AddresseeId { get; set; }
	public User Addressee { get; set; } = null!;

	public FriendLinkStatus Status { get; set; } = FriendLinkStatus.PENDING;

	public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
	public DateTimeOffset? RespondedAt { get; set; }
}
