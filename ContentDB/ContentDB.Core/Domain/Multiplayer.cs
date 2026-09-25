// ContentDB C# —— 联机:游戏服务器收录 / 组队房间

namespace ContentDB.Core.Domain;

/// <summary>被收录的游戏服务器。服主注册后凭上报 token 定期推送实时状态。</summary>
public class GameServer
{
	public int Id { get; set; }

	/// <summary>规范化地址(host[:port]),唯一。</summary>
	public string Address { get; set; } = "";

	public string Name { get; set; } = "";
	public string? Description { get; set; }
	public string? WebsiteUrl { get; set; }

	public int OwnerId { get; set; }
	public User Owner { get; set; } = null!;

	/// <summary>服务器端 mod 上报状态用的 token(存 SHA-256,明文仅生成时返回一次)。</summary>
	public string? ReportTokenHash { get; set; }
	public string? ReportTokenPrefix { get; set; }

	// ---- 最近一次上报的实时状态 ----
	public int PlayersOnline { get; set; }
	public int PlayersMax { get; set; }
	public string? Motd { get; set; }
	public DateTimeOffset? ReportedAt { get; set; }

	/// <summary>是否公开列出(收录但暂不上架)。</summary>
	public bool Listed { get; set; } = true;

	/// <summary>管理员认证标记。</summary>
	public bool Verified { get; set; }

	public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

	/// <summary>10 分钟内有上报视为在线。</summary>
	public bool IsOnline => ReportedAt is not null
		&& DateTimeOffset.UtcNow - ReportedAt.Value < TimeSpan.FromMinutes(10);
}

public enum PartyStatus
{
	ACTIVE,
	ENDED,
}

/// <summary>组队房间:队长建房间得到邀请码,成员进房;队长设置目标服务器,成员客户端轮询后可一键加入。</summary>
public class Party
{
	public int Id { get; set; }

	/// <summary>6 位邀请码,唯一。</summary>
	public string Code { get; set; } = "";

	public int LeaderId { get; set; }
	public User Leader { get; set; } = null!;

	/// <summary>队长设置的目标服务器地址(可空 = 待定)。</summary>
	public string? ServerAddress { get; set; }

	public PartyStatus Status { get; set; } = PartyStatus.ACTIVE;

	public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
	public DateTimeOffset? EndedAt { get; set; }

	public ICollection<PartyMember> Members { get; set; } = new List<PartyMember>();
}

public class PartyMember
{
	public int Id { get; set; }

	public int PartyId { get; set; }
	public Party Party { get; set; } = null!;

	public int UserId { get; set; }
	public User User { get; set; } = null!;

	public DateTimeOffset JoinedAt { get; set; } = DateTimeOffset.UtcNow;
}
