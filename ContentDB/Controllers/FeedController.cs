// ContentDB C# —— RSS/Atom feeds + Prometheus metrics + 审计日志读取
// feeds:最新发布(本站);metrics:基础计数(Prometheus 文本格式);audit:管理员查看审计日志。

using System.Text;
using System.Xml;
using ContentDB.Api.Auth;
using ContentDB.Core.Domain;
using ContentDB.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ContentDB.Api.Controllers;

[ApiController]
public sealed class FeedController : ControllerBase
{
	private readonly AppDbContext _db;

	public FeedController(AppDbContext db) => _db = db;

	/// <summary>最新本站发布的 RSS feed。</summary>
	[HttpGet("/feed/releases.rss")]
	public async Task<IActionResult> ReleasesRss()
	{
		var items = await _db.Releases
			.Where(r => r.State == ReleaseState.APPROVED && r.Package.State == PackageState.APPROVED)
			.OrderByDescending(r => r.CreatedAt)
			.Take(50)
			.Select(r => new
			{
				r.Title,
				r.CreatedAt,
				Author = r.Package.Author.Username,
				PackageName = r.Package.Name,
				PackageTitle = r.Package.Title,
				r.Id,
			})
			.ToListAsync(HttpContext.RequestAborted);

		var sb = new StringBuilder();
		using var w = XmlWriter.Create(sb, new XmlWriterSettings { Indent = true, Async = false });
		w.WriteStartDocument();
		w.WriteStartElement("rss");
		w.WriteAttributeString("version", "2.0");
		w.WriteStartElement("channel");
		w.WriteElementString("title", "ContentDB 镜像 · 最新发布");
		w.WriteElementString("description", "本站最新已审核发布");
		w.WriteElementString("link", "/");

		foreach (var it in items)
		{
			w.WriteStartElement("item");
			w.WriteElementString("title", $"{it.PackageTitle}: {it.Title}");
			w.WriteElementString("link", $"/packages/{it.Author}/{it.PackageName}/");
			w.WriteElementString("guid", $"/packages/{it.Author}/{it.PackageName}/releases/{it.Id}/");
			w.WriteElementString("pubDate", it.CreatedAt.ToString("r"));
			w.WriteEndElement();
		}

		w.WriteEndElement(); // channel
		w.WriteEndElement(); // rss
		w.WriteEndDocument();
		w.Flush();

		return Content(sb.ToString(), "application/rss+xml");
	}

	/// <summary>Prometheus 文本格式基础指标。</summary>
	[HttpGet("/metrics")]
	public async Task<IActionResult> Metrics()
	{
		var ct = HttpContext.RequestAborted;
		var packages = await _db.Packages.CountAsync(p => p.State == PackageState.APPROVED, ct);
		var releases = await _db.Releases.CountAsync(r => r.State == ReleaseState.APPROVED, ct);
		var users = await _db.Users.CountAsync(ct);
		var downloads = await _db.Packages.SumAsync(p => (long)p.Downloads, ct);

		var sb = new StringBuilder();
		sb.AppendLine("# HELP contentdb_packages Total approved local packages");
		sb.AppendLine("# TYPE contentdb_packages gauge");
		sb.AppendLine($"contentdb_packages {packages}");
		sb.AppendLine("# HELP contentdb_releases Total approved local releases");
		sb.AppendLine("# TYPE contentdb_releases gauge");
		sb.AppendLine($"contentdb_releases {releases}");
		sb.AppendLine("# HELP contentdb_users Total users");
		sb.AppendLine("# TYPE contentdb_users gauge");
		sb.AppendLine($"contentdb_users {users}");
		sb.AppendLine("# HELP contentdb_downloads_total Total local package downloads");
		sb.AppendLine("# TYPE contentdb_downloads_total counter");
		sb.AppendLine($"contentdb_downloads_total {downloads}");

		return Content(sb.ToString(), "text/plain; version=0.0.4");
	}
}

[ApiController]
public sealed class AuditController : ApiControllerBase
{
	private readonly AppDbContext _db;

	public AuditController(ICurrentUserAccessor currentUser, AppDbContext db) : base(currentUser)
		=> _db = db;

	/// <summary>管理员/编辑查看审计日志。</summary>
	[HttpGet("/api/audit/")]
	public async Task<IActionResult> List()
	{
		var (user, err) = await RequireUserAsync();
		if (err is not null) return err;
		if (!user!.Rank.AtLeast(UserRank.EDITOR))
			return ApiError(403, "Editor rank required");

		var limit = int.TryParse(Request.Query["n"], out var n) ? Math.Clamp(n, 1, 500) : 100;

		var items = await _db.AuditLog
			.OrderByDescending(a => a.CreatedAt)
			.Take(limit)
			.Select(a => new
			{
				a.Id,
				a.Title,
				severity = a.Severity.ToString(),
				a.Url,
				a.Description,
				causer = a.Causer != null ? a.Causer.Username : null,
				package = a.Package != null ? a.Package.Author.Username + "/" + a.Package.Name : null,
				created_at = a.CreatedAt,
			})
			.ToListAsync(HttpContext.RequestAborted);

		return Ok(items);
	}
}

[ApiController]
public sealed class ModerationController : ApiControllerBase
{
	private readonly AppDbContext _db;

	public ModerationController(ICurrentUserAccessor currentUser, AppDbContext db) : base(currentUser)
		=> _db = db;

	/// <summary>
	/// 审核队列 / 按状态列出本站包(需 EDITOR+)。默认 ready_for_review。
	/// ?state= 可传 wip / changes_needed / ready_for_review / approved / deleted。
	/// </summary>
	[HttpGet("/api/admin/packages/")]
	public async Task<IActionResult> ListByState()
	{
		var (user, err) = await RequireUserAsync();
		if (err is not null) return err;
		if (!user!.Rank.AtLeast(UserRank.EDITOR))
			return ApiError(403, "Editor rank required");

		var stateArg = Request.Query["state"].ToString();
		if (string.IsNullOrEmpty(stateArg)) stateArg = "ready_for_review";
		if (!Enum.TryParse<PackageState>(stateArg, ignoreCase: true, out var state))
			return ApiError(400, "Unknown state");

		var limit = int.TryParse(Request.Query["n"], out var n) ? Math.Clamp(n, 1, 500) : 100;

		var items = await _db.Packages
			.Where(p => p.State == state)
			.OrderByDescending(p => p.CreatedAt)
			.Take(limit)
			.Select(p => new
			{
				author = p.Author.Username,
				p.Name,
				p.Title,
				short_description = p.ShortDesc,
				type = p.Type.ToString().ToLower(),
				state = p.State.ToString().ToLower(),
				created_at = p.CreatedAt,
				approved_at = p.ApprovedAt,
			})
			.ToListAsync(HttpContext.RequestAborted);

		return Ok(items);
	}
}
