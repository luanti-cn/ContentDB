// ContentDB C# —— 包写端点(创建/编辑/删除/审核状态迁移)
// 仅作用于本站(LOCAL)内容;上游包只读。鉴权:Bearer API Token 或 Cookie(OIDC)会话。

using ContentDB.Api.Auth;
using ContentDB.Core.Abstractions;
using ContentDB.Core.Domain;
using Microsoft.AspNetCore.Mvc;

namespace ContentDB.Api.Controllers;

[ApiController]
public sealed class PackageWriteController : ApiControllerBase
{
	private readonly IPackageWriteService _packages;

	public PackageWriteController(ICurrentUserAccessor currentUser, IPackageWriteService packages)
		: base(currentUser)
	{
		_packages = packages;
	}

	public sealed record CreatePackageBody(
		string Name, string? Title, string? ShortDescription, string? LongDescription,
		string? Type, string? License, string? MediaLicense,
		string? Repo, string? Website, string? IssueTracker, int? Forums,
		string? VideoUrl, string? DonateUrl, string? TranslationUrl, string? DevState,
		List<string>? Tags, List<string>? ContentWarnings);

	private static PackageEditInput ToInput(CreatePackageBody b) => new(
		b.Title, b.ShortDescription, b.LongDescription, b.Type, b.License, b.MediaLicense,
		b.Repo, b.Website, b.IssueTracker, b.Forums, b.VideoUrl, b.DonateUrl, b.TranslationUrl,
		b.DevState, b.Tags, b.ContentWarnings);

	/// <summary>创建包(本站)。</summary>
	[HttpPost("/api/packages/")]
	public async Task<IActionResult> Create([FromBody] CreatePackageBody body)
	{
		var (user, err) = await RequireUserAsync();
		if (err is not null) return err;
		if (string.IsNullOrWhiteSpace(body.Name)) return ApiError(400, "name is required");

		var result = await _packages.CreateAsync(user!, body.Name, ToInput(body), HttpContext.RequestAborted);
		return FromResult(result);
	}

	/// <summary>编辑包(本站)。</summary>
	[HttpPut("/api/packages/{author}/{name}/")]
	public async Task<IActionResult> Edit(string author, string name, [FromBody] CreatePackageBody body)
	{
		var (user, err) = await RequireUserAsync();
		if (err is not null) return err;

		var pkg = await _packages.FindAsync(author, name, HttpContext.RequestAborted);
		if (pkg is null) return ApiError(404, "Package not found (write ops only apply to local packages)");

		var result = await _packages.EditAsync(user!, pkg, ToInput(body), HttpContext.RequestAborted);
		return FromResult(result);
	}

	/// <summary>删除包(本站,软删除为 DELETED)。</summary>
	[HttpDelete("/api/packages/{author}/{name}/")]
	public async Task<IActionResult> Delete(string author, string name)
	{
		var (user, err) = await RequireUserAsync();
		if (err is not null) return err;

		var pkg = await _packages.FindAsync(author, name, HttpContext.RequestAborted);
		if (pkg is null) return ApiError(404, "Package not found");

		var result = await _packages.DeleteAsync(user!, pkg, HttpContext.RequestAborted);
		return FromResult(result);
	}

	public sealed record MoveStateBody(string State);

	/// <summary>审核状态迁移。state ∈ {wip, changes_needed, ready_for_review, approved, deleted}。</summary>
	[HttpPost("/api/packages/{author}/{name}/state/")]
	public async Task<IActionResult> MoveState(string author, string name, [FromBody] MoveStateBody body)
	{
		var (user, err) = await RequireUserAsync();
		if (err is not null) return err;

		if (!Enum.TryParse<PackageState>(body.State, ignoreCase: true, out var target))
			return ApiError(400, "Unknown state");

		var pkg = await _packages.FindAsync(author, name, HttpContext.RequestAborted);
		if (pkg is null) return ApiError(404, "Package not found");

		var result = await _packages.MoveToStateAsync(user!, pkg, target, HttpContext.RequestAborted);
		return FromResult(result);
	}
}
