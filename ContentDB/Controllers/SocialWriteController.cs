// ContentDB C# —— 线程 / 评价 / 合集 写端点

using ContentDB.Api.Auth;
using ContentDB.Core.Abstractions;
using ContentDB.Core.Domain;
using ContentDB.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ContentDB.Api.Controllers;

[ApiController]
public sealed class ThreadReadController : ApiControllerBase
{
	private readonly AppDbContext _db;

	public ThreadReadController(ICurrentUserAccessor currentUser, AppDbContext db)
		: base(currentUser) => _db = db;

	/// <summary>线程列表。支持 ?author=&name= 过滤某包下的线程;默认排除私有(除非有权限)。</summary>
	[HttpGet("/api/threads/")]
	public async Task<IActionResult> List()
	{
		var user = await GetUserAsync();
		var author = Request.Query["author"].ToString();
		var name = Request.Query["name"].ToString();

		var q = _db.Threads
			.Include(t => t.Author)
			.Include(t => t.Package).ThenInclude(p => p!.Author)
			.AsQueryable();

		if (!string.IsNullOrEmpty(author) && !string.IsNullOrEmpty(name))
			q = q.Where(t => t.Package != null
				&& t.Package.Author.Username == author && t.Package.Name == name);

		var threads = await q
			.OrderByDescending(t => t.CreatedAt)
			.Take(200)
			.ToListAsync(HttpContext.RequestAborted);

		// 私有线程按权限过滤(内存里判断,数量已受 Take 限制)
		var visible = threads
			.Where(t => t.CheckPerm(user, Permission.SEE_THREAD))
			.Select(t => new
			{
				id = t.Id,
				title = t.Title,
				is_private = t.Private,
				locked = t.Locked,
				author = t.Author.Username,
				package = t.Package != null ? t.Package.Author.Username + "/" + t.Package.Name : null,
				created_at = t.CreatedAt,
			});

		return Ok(visible);
	}

	/// <summary>线程详情(含回复)。私有线程需权限。</summary>
	[HttpGet("/api/threads/{id:int}/")]
	public async Task<IActionResult> View(int id)
	{
		var user = await GetUserAsync();

		var thread = await _db.Threads
			.Include(t => t.Author)
			.Include(t => t.Package).ThenInclude(p => p!.Author)
			.Include(t => t.Replies).ThenInclude(r => r.Author)
			.FirstOrDefaultAsync(t => t.Id == id, HttpContext.RequestAborted);

		if (thread is null) return ApiError(404, "Thread not found");
		if (!thread.CheckPerm(user, Permission.SEE_THREAD))
			return ApiError(403, "You cannot see this thread");

		var canComment = thread.CheckPerm(user, Permission.COMMENT_THREAD);

		return Ok(new
		{
			id = thread.Id,
			title = thread.Title,
			is_private = thread.Private,
			locked = thread.Locked,
			author = thread.Author.Username,
			package = thread.Package != null
				? thread.Package.Author.Username + "/" + thread.Package.Name : null,
			created_at = thread.CreatedAt,
			can_comment = canComment,
			replies = thread.Replies
				.OrderBy(r => r.CreatedAt)
				.Select(r => new
				{
					id = r.Id,
					comment = r.Comment,
					author = r.Author.Username,
					is_status_update = r.IsStatusUpdate,
					created_at = r.CreatedAt,
				}),
		});
	}
}

[ApiController]
public sealed class ThreadWriteController : ApiControllerBase
{
	private readonly IThreadWriteService _threads;

	public ThreadWriteController(ICurrentUserAccessor currentUser, IThreadWriteService threads)
		: base(currentUser) => _threads = threads;

	public sealed record CreateThreadBody(
		string? PackageAuthor, string? PackageName, string Title, string Comment, bool Private = false);

	[HttpPost("/api/threads/new/")]
	public async Task<IActionResult> Create([FromBody] CreateThreadBody body)
	{
		var (user, err) = await RequireUserAsync();
		if (err is not null) return err;
		if (string.IsNullOrWhiteSpace(body.Title) || string.IsNullOrWhiteSpace(body.Comment))
			return ApiError(400, "title and comment are required");

		return FromResult(await _threads.CreateThreadAsync(
			user!, body.PackageAuthor, body.PackageName, body.Title, body.Comment, body.Private,
			HttpContext.RequestAborted));
	}

	public sealed record ReplyBody(string Comment);

	[HttpPost("/api/threads/{id:int}/reply/")]
	public async Task<IActionResult> Reply(int id, [FromBody] ReplyBody body)
	{
		var (user, err) = await RequireUserAsync();
		if (err is not null) return err;
		if (string.IsNullOrWhiteSpace(body.Comment)) return ApiError(400, "comment is required");
		return FromResult(await _threads.ReplyAsync(user!, id, body.Comment, HttpContext.RequestAborted));
	}

	public sealed record LockBody(bool Locked);

	[HttpPost("/api/threads/{id:int}/lock/")]
	public async Task<IActionResult> Lock(int id, [FromBody] LockBody body)
	{
		var (user, err) = await RequireUserAsync();
		if (err is not null) return err;
		return FromResult(await _threads.SetLockedAsync(user!, id, body.Locked, HttpContext.RequestAborted));
	}
}

[ApiController]
public sealed class ReviewWriteController : ApiControllerBase
{
	private readonly IPackageWriteService _packages;
	private readonly IReviewWriteService _reviews;

	public ReviewWriteController(
		ICurrentUserAccessor currentUser, IPackageWriteService packages, IReviewWriteService reviews)
		: base(currentUser)
	{
		_packages = packages;
		_reviews = reviews;
	}

	public sealed record ReviewBody(int Rating, string Title, string Comment, string? Language);

	[HttpPost("/api/packages/{author}/{name}/reviews/")]
	public async Task<IActionResult> CreateOrUpdate(string author, string name, [FromBody] ReviewBody body)
	{
		var (user, err) = await RequireUserAsync();
		if (err is not null) return err;
		var pkg = await _packages.FindAsync(author, name, HttpContext.RequestAborted);
		if (pkg is null) return ApiError(404, "Package not found");
		return FromResult(await _reviews.CreateOrUpdateAsync(
			user!, pkg, body.Rating, body.Title, body.Comment, body.Language, HttpContext.RequestAborted));
	}

	[HttpDelete("/api/packages/{author}/{name}/reviews/")]
	public async Task<IActionResult> Delete(string author, string name)
	{
		var (user, err) = await RequireUserAsync();
		if (err is not null) return err;
		var pkg = await _packages.FindAsync(author, name, HttpContext.RequestAborted);
		if (pkg is null) return ApiError(404, "Package not found");
		return FromResult(await _reviews.DeleteAsync(user!, pkg, HttpContext.RequestAborted));
	}

	public sealed record VoteBody(bool IsPositive);

	[HttpPost("/api/reviews/{id:int}/vote/")]
	public async Task<IActionResult> Vote(int id, [FromBody] VoteBody body)
	{
		var (user, err) = await RequireUserAsync();
		if (err is not null) return err;
		return FromResult(await _reviews.VoteAsync(user!, id, body.IsPositive, HttpContext.RequestAborted));
	}
}

[ApiController]
public sealed class CollectionWriteController : ApiControllerBase
{
	private readonly ICollectionWriteService _collections;

	public CollectionWriteController(ICurrentUserAccessor currentUser, ICollectionWriteService collections)
		: base(currentUser) => _collections = collections;

	public sealed record CreateCollectionBody(
		string Name, string Title, string ShortDescription, string? LongDescription, bool Private = false);

	[HttpPost("/api/collections/")]
	public async Task<IActionResult> Create([FromBody] CreateCollectionBody body)
	{
		var (user, err) = await RequireUserAsync();
		if (err is not null) return err;
		return FromResult(await _collections.CreateAsync(
			user!, body.Name, body.Title, body.ShortDescription, body.LongDescription, body.Private,
			HttpContext.RequestAborted));
	}

	public sealed record EditCollectionBody(string? Title, string? ShortDescription, string? LongDescription, bool? Private);

	[HttpPut("/api/collections/{author}/{name}/")]
	public async Task<IActionResult> Edit(string author, string name, [FromBody] EditCollectionBody body)
	{
		var (user, err) = await RequireUserAsync();
		if (err is not null) return err;
		return FromResult(await _collections.EditAsync(
			user!, author, name, body.Title, body.ShortDescription, body.LongDescription, body.Private,
			HttpContext.RequestAborted));
	}

	[HttpDelete("/api/collections/{author}/{name}/")]
	public async Task<IActionResult> Delete(string author, string name)
	{
		var (user, err) = await RequireUserAsync();
		if (err is not null) return err;
		return FromResult(await _collections.DeleteAsync(user!, author, name, HttpContext.RequestAborted));
	}

	public sealed record AddPackageBody(string PackageAuthor, string PackageName, string? Description);

	[HttpPost("/api/collections/{author}/{name}/add/")]
	public async Task<IActionResult> AddPackage(string author, string name, [FromBody] AddPackageBody body)
	{
		var (user, err) = await RequireUserAsync();
		if (err is not null) return err;
		return FromResult(await _collections.AddPackageAsync(
			user!, author, name, body.PackageAuthor, body.PackageName, body.Description, HttpContext.RequestAborted));
	}

	public sealed record RemovePackageBody(string PackageAuthor, string PackageName);

	[HttpPost("/api/collections/{author}/{name}/remove/")]
	public async Task<IActionResult> RemovePackage(string author, string name, [FromBody] RemovePackageBody body)
	{
		var (user, err) = await RequireUserAsync();
		if (err is not null) return err;
		return FromResult(await _collections.RemovePackageAsync(
			user!, author, name, body.PackageAuthor, body.PackageName, HttpContext.RequestAborted));
	}
}
