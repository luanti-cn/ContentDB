// ContentDB C# —— 包写服务实现

using ContentDB.Core.Abstractions;
using ContentDB.Core.Domain;
using ContentDB.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace ContentDB.Infrastructure.Services;

public sealed partial class PackageWriteService : IPackageWriteService
{
	private readonly AppDbContext _db;
	private readonly ILogger<PackageWriteService> _logger;

	[GeneratedRegex("^[a-z0-9_]+$")]
	private static partial Regex NameRegex();

	public PackageWriteService(AppDbContext db, ILogger<PackageWriteService> logger)
	{
		_db = db;
		_logger = logger;
	}

	public Task<Package?> FindAsync(string author, string name, CancellationToken ct = default)
		=> _db.Packages
			.Include(p => p.Author)
			.Include(p => p.Maintainers)
			.Include(p => p.Tags)
			.Include(p => p.ContentWarnings)
			.Include(p => p.Releases)
			.FirstOrDefaultAsync(p => p.Author.Username == author && p.Name == name, ct);

	public async Task<ServiceResult> CreateAsync(User actor, string name, PackageEditInput input, CancellationToken ct = default)
	{
		if (!actor.Rank.AtLeast(UserRank.NEW_MEMBER))
			return ServiceResult.Fail(403, "Insufficient rank to create packages");

		name = name.ToLowerInvariant();
		if (!NameRegex().IsMatch(name) || name == "_game")
			return ServiceResult.Fail(400, "Invalid package name (must match ^[a-z0-9_]+$)");

		if (await _db.Packages.AnyAsync(p => p.AuthorId == actor.Id && p.Name == name, ct))
			return ServiceResult.Fail(409, "You already have a package with that name");

		var type = PackageTypeExtensions.Parse(input.Type) ?? PackageType.MOD;

		// 保底:确保 License/MediaLicense 指向存在的行(空 DB 下默认 id=1 可能不存在,会触发 FK 违约)。
		var defaultLicenseId = await EnsureDefaultLicenseIdAsync(ct);

		var pkg = new Package
		{
			AuthorId = actor.Id,
			Name = name,
			Title = input.Title ?? name,
			ShortDesc = input.ShortDescription ?? "",
			Desc = input.LongDescription,
			Type = type,
			State = PackageState.WIP,
			CreatedAt = DateTimeOffset.UtcNow,
			LicenseId = defaultLicenseId,
			MediaLicenseId = defaultLicenseId,
		};
		pkg.Maintainers.Add(actor);

		await ApplyEditableFieldsAsync(pkg, input, ct);

		_db.Packages.Add(pkg);
		await AuditAsync(actor, pkg, AuditSeverity.NORMAL, $"Created package {actor.Username}/{name}", ct);
		await _db.SaveChangesAsync(ct);

		return ServiceResult.Ok(new { author = actor.Username, name = pkg.Name });
	}

	public async Task<ServiceResult> EditAsync(User actor, Package package, PackageEditInput input, CancellationToken ct = default)
	{
		if (!package.CheckPerm(actor, Permission.EDIT_PACKAGE))
			return ServiceResult.Fail(403, "You do not have permission to edit this package");

		if (input.Title is not null) package.Title = input.Title;
		if (input.ShortDescription is not null) package.ShortDesc = input.ShortDescription;
		if (input.LongDescription is not null) package.Desc = input.LongDescription;
		if (input.Type is not null && PackageTypeExtensions.Parse(input.Type) is { } t) package.Type = t;

		await ApplyEditableFieldsAsync(package, input, ct);

		await AuditAsync(actor, package, AuditSeverity.NORMAL, $"Edited package {package.GetId()}", ct);
		await _db.SaveChangesAsync(ct);
		return ServiceResult.Ok(new { success = true });
	}

	public async Task<ServiceResult> DeleteAsync(User actor, Package package, CancellationToken ct = default)
	{
		if (!package.CheckPerm(actor, Permission.DELETE_PACKAGE))
			return ServiceResult.Fail(403, "You do not have permission to delete this package");

		package.State = PackageState.DELETED;
		await AuditAsync(actor, package, AuditSeverity.MODERATION, $"Deleted package {package.GetId()}", ct);
		await _db.SaveChangesAsync(ct);
		return ServiceResult.Ok(new { success = true });
	}

	public async Task<ServiceResult> MoveToStateAsync(User actor, Package package, PackageState target, CancellationToken ct = default)
	{
		if (!CanMoveToState(actor, package, target, out var reason))
			return ServiceResult.Fail(403, reason);

		var prev = package.State;
		package.State = target;

		if (target == PackageState.APPROVED && package.ApprovedAt is null)
			package.ApprovedAt = DateTimeOffset.UtcNow;

		await AuditAsync(actor, package,
			target is PackageState.APPROVED or PackageState.DELETED ? AuditSeverity.EDITOR : AuditSeverity.NORMAL,
			$"Package {package.GetId()} moved {prev} -> {target}", ct);
		await _db.SaveChangesAsync(ct);

		return ServiceResult.Ok(new { state = target.ToString() });
	}

	/// <summary>审核状态机(移植 domain/package_approval.can_move_to_state 的要点)。</summary>
	private static bool CanMoveToState(User user, Package package, PackageState target, out string reason)
	{
		reason = "";
		bool isApprover = user.Rank.AtLeast(UserRank.APPROVER);
		bool isMaintainer = package.AuthorId == user.Id
			|| package.Maintainers.Any(m => m.Id == user.Id)
			|| user.Rank.AtLeast(UserRank.EDITOR);

		switch (target)
		{
			case PackageState.READY_FOR_REVIEW:
			case PackageState.WIP:
			case PackageState.CHANGES_NEEDED:
				if (!isMaintainer && !isApprover) { reason = "Not a maintainer"; return false; }
				// 提交审核需具备基本信息与至少一个发布
				if (target == PackageState.READY_FOR_REVIEW)
				{
					if (string.IsNullOrWhiteSpace(package.ShortDesc)) { reason = "Missing short description"; return false; }
					if (!package.Releases.Any()) { reason = "Needs at least one release"; return false; }
				}
				return true;

			case PackageState.APPROVED:
				if (!isApprover) { reason = "Only approvers can approve packages"; return false; }
				if (!package.Releases.Any(r => r.State == ReleaseState.APPROVED))
				{ reason = "Needs an approved release"; return false; }
				return true;

			case PackageState.DELETED:
				if (!(package.AuthorId == user.Id || user.Rank.AtLeast(UserRank.EDITOR)))
				{ reason = "Not permitted to delete"; return false; }
				return true;

			default:
				reason = "Unknown target state";
				return false;
		}
	}

	private async Task ApplyEditableFieldsAsync(Package pkg, PackageEditInput input, CancellationToken ct)
	{
		if (input.License is not null)
			pkg.LicenseId = await ResolveLicenseIdAsync(input.License, ct) ?? pkg.LicenseId;
		if (input.MediaLicense is not null)
			pkg.MediaLicenseId = await ResolveLicenseIdAsync(input.MediaLicense, ct) ?? pkg.MediaLicenseId;

		if (input.Repo is not null) pkg.Repo = Empty(input.Repo);
		if (input.Website is not null) pkg.Website = Empty(input.Website);
		if (input.IssueTracker is not null) pkg.IssueTracker = Empty(input.IssueTracker);
		if (input.Forums is not null) pkg.Forums = input.Forums;
		if (input.VideoUrl is not null) pkg.VideoUrl = Empty(input.VideoUrl);
		if (input.DonateUrl is not null) pkg.DonateUrl = Empty(input.DonateUrl);
		if (input.TranslationUrl is not null) pkg.TranslationUrl = Empty(input.TranslationUrl);

		if (input.DevState is not null)
			pkg.DevState = Enum.TryParse<PackageDevState>(input.DevState, true, out var ds) ? ds : null;

		if (input.Tags is not null)
		{
			pkg.Tags.Clear();
			var tags = await _db.Tags.Where(t => input.Tags.Contains(t.Name)).ToListAsync(ct);
			foreach (var t in tags) pkg.Tags.Add(t);
		}

		if (input.ContentWarnings is not null)
		{
			pkg.ContentWarnings.Clear();
			var cws = await _db.ContentWarnings.Where(w => input.ContentWarnings.Contains(w.Name)).ToListAsync(ct);
			foreach (var w in cws) pkg.ContentWarnings.Add(w);
		}
	}

	/// <summary>按名称解析许可证 id;不存在则创建(避免 FK 违约)。空名回退到 "Other"。</summary>
	private async Task<int?> ResolveLicenseIdAsync(string name, CancellationToken ct)
	{
		name = string.IsNullOrWhiteSpace(name) ? "Other" : name.Trim();

		var existing = await _db.Licenses
			.Where(l => l.Name.ToLower() == name.ToLower())
			.Select(l => (int?)l.Id)
			.FirstOrDefaultAsync(ct);
		if (existing is not null) return existing;

		var created = new License { Name = name, IsFoss = true };
		_db.Licenses.Add(created);
		try
		{
			await _db.SaveChangesAsync(ct);
			return created.Id;
		}
		catch (DbUpdateException)
		{
			// 并发下另一个请求可能已创建同名 license(命中 license.Name 唯一索引)。
			// 撤销本地待插入实体后重查;若确为竞态则回退到已存在的行 id,否则重新抛出。
			_db.Entry(created).State = EntityState.Detached;
			var raced = await LicenseExistsAsync(name, ct);
			if (raced is not null) return raced;
			throw;
		}
	}

	/// <summary>按名称(不区分大小写)查已存在的 license id;不存在返回 null。</summary>
	private async Task<int?> LicenseExistsAsync(string name, CancellationToken ct)
		=> await _db.Licenses
			.Where(l => l.Name.ToLower() == name.ToLower())
			.Select(l => (int?)l.Id)
			.FirstOrDefaultAsync(ct);

	/// <summary>确保有一个可用的默认许可证行,返回其 id(用于未指定许可证时的 FK 保底)。</summary>
	private async Task<int> EnsureDefaultLicenseIdAsync(CancellationToken ct)
		=> await ResolveLicenseIdAsync("Other", ct) ?? throw new InvalidOperationException("failed to ensure default license");

	private static string? Empty(string s) => string.IsNullOrWhiteSpace(s) ? null : s;

	private async Task AuditAsync(User actor, Package pkg, AuditSeverity sev, string title, CancellationToken ct)
	{
		_db.AuditLog.Add(new AuditLogEntry
		{
			CauserId = actor.Id,
			PackageId = pkg.Id == 0 ? null : pkg.Id,
			Severity = sev,
			Title = title,
			CreatedAt = DateTimeOffset.UtcNow,
		});
		await Task.CompletedTask;
	}
}
