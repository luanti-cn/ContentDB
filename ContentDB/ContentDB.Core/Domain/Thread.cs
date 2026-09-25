// ContentDB C# —— 线程 / 回复 / 评价 / 评价投票

namespace ContentDB.Core.Domain;

public class Thread
{
	public int Id { get; set; }

	public int? PackageId { get; set; }
	public Package? Package { get; set; }

	public int? ReviewId { get; set; }
	public PackageReview? Review { get; set; }

	public int AuthorId { get; set; }
	public User Author { get; set; } = null!;

	public string Title { get; set; } = "";
	public bool Private { get; set; }
	public bool Locked { get; set; }
	public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

	public ICollection<ThreadReply> Replies { get; set; } = new List<ThreadReply>();
	public ICollection<User> Watchers { get; set; } = new List<User>();

	public bool CheckPerm(User? user, Permission perm)
	{
		bool canSee = !Private
			|| (user is not null && (user.Id == AuthorId || user.Rank.AtLeast(UserRank.APPROVER)));

		if (perm == Permission.SEE_THREAD) return canSee;

		if (user is null || user.IsBanned) return false;

		return perm switch
		{
			Permission.COMMENT_THREAD => canSee && !Locked && user.Rank.AtLeast(UserRank.NEW_MEMBER),
			Permission.LOCK_THREAD or Permission.DELETE_THREAD => user.Rank.AtLeast(UserRank.MODERATOR),
			_ => throw new InvalidOperationException($"Permission {perm} is not related to threads"),
		};
	}
}

public class ThreadReply
{
	public int Id { get; set; }

	public int ThreadId { get; set; }
	public Thread Thread { get; set; } = null!;

	public string Comment { get; set; } = "";  // <= 2000 chars

	public int AuthorId { get; set; }
	public User Author { get; set; } = null!;

	public bool IsStatusUpdate { get; set; }
	public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class PackageReview
{
	public int Id { get; set; }

	public int PackageId { get; set; }
	public Package Package { get; set; } = null!;

	public int AuthorId { get; set; }
	public User Author { get; set; } = null!;

	public string? LanguageId { get; set; }
	public bool Approved { get; set; }
	public int Rating { get; set; }  // 1..5, >3 正面, ==3 中性, <3 负面

	public int? ThreadId { get; set; }
	public Thread? Thread { get; set; }

	public int Votes { get; set; }
	public double Score { get; set; }

	public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

	public ICollection<PackageReviewVote> ReviewVotes { get; set; } = new List<PackageReviewVote>();

	public double AsWeight() => Rating switch { > 3 => 1.0, 3 => 0.0, _ => -1.0 };
}

public class PackageReviewVote
{
	public int ReviewId { get; set; }
	public PackageReview Review { get; set; } = null!;

	public int UserId { get; set; }
	public User User { get; set; } = null!;

	public bool IsPositive { get; set; }
}
