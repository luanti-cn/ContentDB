// ContentDB C# —— 好友私聊消息
// 仅好友可互发;已读时间用于回执;历史永久保留(清理任务后续可加)。

namespace ContentDB.Core.Domain;

public class DirectMessage
{
	public long Id { get; set; }

	public int SenderId { get; set; }
	public User Sender { get; set; } = null!;

	public int RecipientId { get; set; }
	public User Recipient { get; set; } = null!;

	public string Body { get; set; } = "";

	public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
	public DateTimeOffset? ReadAt { get; set; }
}
