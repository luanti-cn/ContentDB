// ContentDB C# —— 合集 / 合集包

namespace ContentDB.Core.Domain;

public class Collection
{
	public int Id { get; set; }

	public int AuthorId { get; set; }
	public User Author { get; set; } = null!;

	public string Name { get; set; } = "";   // ^[a-z0-9_]+$
	public string Title { get; set; } = "";
	public string ShortDescription { get; set; } = "";
	public string? LongDescription { get; set; }
	public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
	public bool Private { get; set; }
	public bool Pinned { get; set; }

	public ICollection<CollectionPackage> Items { get; set; } = new List<CollectionPackage>();

	public bool CheckPerm(User? user, Permission perm)
	{
		if (user is null)
			return perm == Permission.VIEW_COLLECTION && !Private;

		bool canView = !Private || AuthorId == user.Id || user.Rank.AtLeast(UserRank.MODERATOR);

		return perm switch
		{
			Permission.VIEW_COLLECTION => canView,
			Permission.EDIT_COLLECTION => canView && (AuthorId == user.Id || user.Rank.AtLeast(UserRank.EDITOR)),
			_ => throw new InvalidOperationException($"Permission {perm} is not related to collections"),
		};
	}
}

public class CollectionPackage
{
	public int PackageId { get; set; }
	public Package Package { get; set; } = null!;

	public int CollectionId { get; set; }
	public Collection Collection { get; set; } = null!;

	public int Order { get; set; }
	public string? Description { get; set; }
	public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
