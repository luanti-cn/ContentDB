// ContentDB C# —— 线程 / 评价 / 合集 写服务实现

using ContentDB.Core.Abstractions;
using ContentDB.Core.Domain;
using ContentDB.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;
using Thread = ContentDB.Core.Domain.Thread;

namespace ContentDB.Infrastructure.Services;

public sealed class ThreadWriteService : IThreadWriteService
{
	private readonly AppDbContext _db;
	private readonly INotificationService _notifications;
	public ThreadWriteService(AppDbContext db, INotificationService notifications)
	{
		_db = db;
		_notifications = notifications;
	}

	public async Task<ServiceResult> CreateThreadAsync(User actor, string? packageAuthor, string? packageName,
		string title, string firstComment, bool isPrivate, CancellationToken ct = default)
	{
		if (!actor.Rank.AtLeast(UserRank.NEW_MEMBER))
			return ServiceResult.Fail(403, "Insufficient rank to create threads");

		Package? pkg = null;
		if (packageAuthor is not null && packageName is not null)
		{
			pkg = await _db.Packages.Include(p => p.Maintainers)
				.FirstOrDefaultAsync(p => p.Author.Username == packageAuthor && p.Name == packageName, ct);
			if (pkg is null) return ServiceResult.Fail(404, "Package not found");
		}

		if (isPrivate && pkg is null && !actor.Rank.AtLeast(UserRank.APPROVER))
			return ServiceResult.Fail(403, "Cannot create private thread here");

		var thread = new Thread
		{
			PackageId = pkg?.Id,
			AuthorId = actor.Id,
			Title = title,
			Private = isPrivate,
			CreatedAt = DateTimeOffset.UtcNow,
		};
		_db.Threads.Add(thread);
		await _db.SaveChangesAsync(ct);

		_db.ThreadReplies.Add(new ThreadReply
		{
			ThreadId = thread.Id,
			AuthorId = actor.Id,
			Comment = Truncate(firstComment, 2000),
			CreatedAt = DateTimeOffset.UtcNow,
		});
		await _db.SaveChangesAsync(ct);

		return ServiceResult.Ok(new { id = thread.Id });
	}

	public async Task<ServiceResult> ReplyAsync(User actor, int threadId, string comment, CancellationToken ct = default)
	{
		var thread = await _db.Threads.FirstOrDefaultAsync(t => t.Id == threadId, ct);
		if (thread is null) return ServiceResult.Fail(404, "Thread not found");
		if (!thread.CheckPerm(actor, Permission.COMMENT_THREAD))
			return ServiceResult.Fail(403, "You cannot comment on this thread");

		_db.ThreadReplies.Add(new ThreadReply
		{
			ThreadId = thread.Id,
			AuthorId = actor.Id,
			Comment = Truncate(comment, 2000),
			CreatedAt = DateTimeOffset.UtcNow,
		});
		await _db.SaveChangesAsync(ct);

		// 通知线程作者(若非本人回复)
		if (thread.AuthorId != actor.Id)
			await _notifications.NotifyAsync(thread.AuthorId, actor.Id, NotificationType.THREAD_REPLY,
				$"{actor.Username} replied to \"{thread.Title}\"", $"/threads/{thread.Id}/",
				thread.PackageId, ct);

		return ServiceResult.Ok(new { success = true });
	}

	public async Task<ServiceResult> SetLockedAsync(User actor, int threadId, bool locked, CancellationToken ct = default)
	{
		var thread = await _db.Threads.FirstOrDefaultAsync(t => t.Id == threadId, ct);
		if (thread is null) return ServiceResult.Fail(404, "Thread not found");
		if (!thread.CheckPerm(actor, Permission.LOCK_THREAD))
			return ServiceResult.Fail(403, "No permission to lock");
		thread.Locked = locked;
		await _db.SaveChangesAsync(ct);
		return ServiceResult.Ok(new { locked });
	}

	private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}

public sealed class ReviewWriteService : IReviewWriteService
{
	private readonly AppDbContext _db;
	public ReviewWriteService(AppDbContext db) => _db = db;

	public async Task<ServiceResult> CreateOrUpdateAsync(User actor, Package package, int rating,
		string title, string comment, string? language, CancellationToken ct = default)
	{
		if (!actor.Rank.AtLeast(UserRank.NEW_MEMBER))
			return ServiceResult.Fail(403, "Insufficient rank to review");
		if (package.AuthorId == actor.Id)
			return ServiceResult.Fail(403, "You cannot review your own package");
		if (rating is < 1 or > 5)
			return ServiceResult.Fail(400, "Rating must be 1..5");

		var review = await _db.Reviews.Include(r => r.Thread)
			.FirstOrDefaultAsync(r => r.PackageId == package.Id && r.AuthorId == actor.Id, ct);

		if (review is null)
		{
			var thread = new Thread
			{
				PackageId = package.Id,
				AuthorId = actor.Id,
				Title = title,
				Private = false,
				CreatedAt = DateTimeOffset.UtcNow,
			};
			_db.Threads.Add(thread);
			await _db.SaveChangesAsync(ct);

			_db.ThreadReplies.Add(new ThreadReply { ThreadId = thread.Id, AuthorId = actor.Id, Comment = comment, CreatedAt = DateTimeOffset.UtcNow });

			review = new PackageReview
			{
				PackageId = package.Id,
				AuthorId = actor.Id,
				Rating = rating,
				LanguageId = language,
				Approved = true,
				ThreadId = thread.Id,
				CreatedAt = DateTimeOffset.UtcNow,
			};
			_db.Reviews.Add(review);
		}
		else
		{
			review.Rating = rating;
			review.LanguageId = language;
			if (review.Thread is not null) review.Thread.Title = title;
		}

		await _db.SaveChangesAsync(ct);
		await RecalculateScoreAsync(package.Id, ct);
		return ServiceResult.Ok(new { success = true });
	}

	public async Task<ServiceResult> DeleteAsync(User actor, Package package, CancellationToken ct = default)
	{
		var review = await _db.Reviews.FirstOrDefaultAsync(r => r.PackageId == package.Id && r.AuthorId == actor.Id, ct);
		if (review is null) return ServiceResult.Fail(404, "Review not found");
		_db.Reviews.Remove(review);
		await _db.SaveChangesAsync(ct);
		await RecalculateScoreAsync(package.Id, ct);
		return ServiceResult.Ok(new { success = true });
	}

	public async Task<ServiceResult> VoteAsync(User actor, int reviewId, bool isPositive, CancellationToken ct = default)
	{
		var review = await _db.Reviews.FirstOrDefaultAsync(r => r.Id == reviewId, ct);
		if (review is null) return ServiceResult.Fail(404, "Review not found");
		if (review.AuthorId == actor.Id) return ServiceResult.Fail(403, "Cannot vote on your own review");

		var vote = await _db.ReviewVotes.FirstOrDefaultAsync(v => v.ReviewId == reviewId && v.UserId == actor.Id, ct);
		if (vote is null)
			_db.ReviewVotes.Add(new PackageReviewVote { ReviewId = reviewId, UserId = actor.Id, IsPositive = isPositive });
		else
			vote.IsPositive = isPositive;

		await _db.SaveChangesAsync(ct);
		return ServiceResult.Ok(new { success = true });
	}

	private async Task RecalculateScoreAsync(int packageId, CancellationToken ct)
	{
		var pkg = await _db.Packages.FirstOrDefaultAsync(p => p.Id == packageId, ct);
		if (pkg is null) return;
		var ratings = await _db.Reviews.Where(r => r.PackageId == packageId && r.Approved).Select(r => r.Rating).ToListAsync(ct);
		double reviewScore = ratings.Sum(rt => 150.0 * (rt > 3 ? 1.0 : rt == 3 ? 0.0 : -1.0));
		pkg.Score = pkg.ScoreDownloads + reviewScore;
		await _db.SaveChangesAsync(ct);
	}
}

public sealed partial class CollectionWriteService : ICollectionWriteService
{
	private readonly AppDbContext _db;
	public CollectionWriteService(AppDbContext db) => _db = db;

	[GeneratedRegex("^[a-z0-9_]+$")]
	private static partial Regex NameRegex();

	public async Task<ServiceResult> CreateAsync(User actor, string name, string title, string shortDesc,
		string? longDesc, bool isPrivate, CancellationToken ct = default)
	{
		if (!actor.Rank.AtLeast(UserRank.MEMBER))
			return ServiceResult.Fail(403, "Insufficient rank to create collections");
		name = name.ToLowerInvariant();
		if (!NameRegex().IsMatch(name) || name == "_game")
			return ServiceResult.Fail(400, "Invalid collection name");
		if (await _db.Collections.AnyAsync(c => c.AuthorId == actor.Id && c.Name == name, ct))
			return ServiceResult.Fail(409, "You already have a collection with that name");

		var col = new Collection
		{
			AuthorId = actor.Id,
			Name = name,
			Title = title,
			ShortDescription = shortDesc,
			LongDescription = longDesc,
			Private = isPrivate,
			CreatedAt = DateTimeOffset.UtcNow,
		};
		_db.Collections.Add(col);
		await _db.SaveChangesAsync(ct);
		return ServiceResult.Ok(new { author = actor.Username, name });
	}

	public async Task<ServiceResult> EditAsync(User actor, string author, string name, string? title,
		string? shortDesc, string? longDesc, bool? isPrivate, CancellationToken ct = default)
	{
		var col = await FindAsync(author, name, ct);
		if (col is null) return ServiceResult.Fail(404, "Collection not found");
		if (!col.CheckPerm(actor, Permission.EDIT_COLLECTION))
			return ServiceResult.Fail(403, "No permission");

		if (title is not null) col.Title = title;
		if (shortDesc is not null) col.ShortDescription = shortDesc;
		if (longDesc is not null) col.LongDescription = longDesc;
		if (isPrivate is not null) col.Private = isPrivate.Value;
		await _db.SaveChangesAsync(ct);
		return ServiceResult.Ok(new { success = true });
	}

	public async Task<ServiceResult> DeleteAsync(User actor, string author, string name, CancellationToken ct = default)
	{
		var col = await FindAsync(author, name, ct);
		if (col is null) return ServiceResult.Fail(404, "Collection not found");
		if (!col.CheckPerm(actor, Permission.EDIT_COLLECTION))
			return ServiceResult.Fail(403, "No permission");
		_db.Collections.Remove(col);
		await _db.SaveChangesAsync(ct);
		return ServiceResult.Ok(new { success = true });
	}

	public async Task<ServiceResult> AddPackageAsync(User actor, string author, string name,
		string pkgAuthor, string pkgName, string? description, CancellationToken ct = default)
	{
		var col = await FindAsync(author, name, ct);
		if (col is null) return ServiceResult.Fail(404, "Collection not found");
		if (!col.CheckPerm(actor, Permission.EDIT_COLLECTION))
			return ServiceResult.Fail(403, "No permission");

		var pkg = await _db.Packages.FirstOrDefaultAsync(p => p.Author.Username == pkgAuthor && p.Name == pkgName, ct);
		if (pkg is null) return ServiceResult.Fail(404, "Package not found");

		if (await _db.CollectionPackages.AnyAsync(cp => cp.CollectionId == col.Id && cp.PackageId == pkg.Id, ct))
			return ServiceResult.Ok(new { success = true }); // 幂等

		var maxOrder = await _db.CollectionPackages.Where(cp => cp.CollectionId == col.Id)
			.Select(cp => (int?)cp.Order).MaxAsync(ct) ?? 0;

		_db.CollectionPackages.Add(new CollectionPackage
		{
			CollectionId = col.Id,
			PackageId = pkg.Id,
			Order = maxOrder + 1,
			Description = description,
			CreatedAt = DateTimeOffset.UtcNow,
		});
		await _db.SaveChangesAsync(ct);
		return ServiceResult.Ok(new { success = true });
	}

	public async Task<ServiceResult> RemovePackageAsync(User actor, string author, string name,
		string pkgAuthor, string pkgName, CancellationToken ct = default)
	{
		var col = await FindAsync(author, name, ct);
		if (col is null) return ServiceResult.Fail(404, "Collection not found");
		if (!col.CheckPerm(actor, Permission.EDIT_COLLECTION))
			return ServiceResult.Fail(403, "No permission");

		var cp = await _db.CollectionPackages
			.FirstOrDefaultAsync(x => x.CollectionId == col.Id
				&& x.Package.Author.Username == pkgAuthor && x.Package.Name == pkgName, ct);
		if (cp is null) return ServiceResult.Fail(404, "Package not in collection");
		_db.CollectionPackages.Remove(cp);
		await _db.SaveChangesAsync(ct);
		return ServiceResult.Ok(new { success = true });
	}

	private Task<Collection?> FindAsync(string author, string name, CancellationToken ct)
		=> _db.Collections.FirstOrDefaultAsync(c => c.Author.Username == author && c.Name == name, ct);
}
