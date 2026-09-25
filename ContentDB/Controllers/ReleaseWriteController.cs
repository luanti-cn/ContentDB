// ContentDB C# —— 发布写端点(zip 上传 / git 导入 / 删除 / 审核)

using ContentDB.Api.Auth;
using ContentDB.Core.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace ContentDB.Api.Controllers;

[ApiController]
public sealed class ReleaseWriteController : ApiControllerBase
{
	private readonly IPackageWriteService _packages;
	private readonly IReleaseWriteService _releases;

	public ReleaseWriteController(
		ICurrentUserAccessor currentUser,
		IPackageWriteService packages,
		IReleaseWriteService releases)
		: base(currentUser)
	{
		_packages = packages;
		_releases = releases;
	}

	/// <summary>
	/// 创建发布。两种方式:
	/// - multipart/form-data:file=<zip> + name + title? + release_notes? + commit?
	/// - application/json:{ method:"git", ref, name, title?, release_notes? }
	/// </summary>
	[HttpPost("/api/packages/{author}/{name}/releases/new/")]
	[RequestSizeLimit(150L * 1024 * 1024)]
	public async Task<IActionResult> Create(string author, string name)
	{
		var (user, err) = await RequireUserAsync();
		if (err is not null) return err;

		var pkg = await _packages.FindAsync(author, name, HttpContext.RequestAborted);
		if (pkg is null) return ApiError(404, "Package not found (releases can only be created on local packages)");

		// git 导入(JSON)
		if (Request.HasJsonContentType())
		{
			var body = await Request.ReadFromJsonAsync<GitReleaseBody>(HttpContext.RequestAborted);
			if (body is null || string.IsNullOrWhiteSpace(body.Ref))
				return ApiError(400, "ref is required for git release");

			var relName = body.Name ?? body.Title ?? body.Ref;
			var result = await _releases.CreateVcsReleaseAsync(
				user!, pkg, relName, body.Title ?? relName, body.ReleaseNotes, body.Ref, HttpContext.RequestAborted);
			return FromResult(result);
		}

		// zip 上传(multipart)
		if (Request.HasFormContentType)
		{
			var form = await Request.ReadFormAsync(HttpContext.RequestAborted);
			var file = form.Files.GetFile("file");
			if (file is null) return ApiError(400, "Missing 'file' in multipart body");

			var relName = form["name"].ToString();
			var title = form["title"].ToString();
			if (string.IsNullOrWhiteSpace(relName)) relName = string.IsNullOrWhiteSpace(title) ? file.FileName : title;
			var releaseNotes = form["release_notes"].ToString();
			var commit = form["commit"].ToString();

			await using var stream = file.OpenReadStream();
			var result = await _releases.CreateZipReleaseAsync(
				user!, pkg, relName, string.IsNullOrWhiteSpace(title) ? relName : title,
				string.IsNullOrWhiteSpace(releaseNotes) ? null : releaseNotes,
				stream, file.Length, string.IsNullOrWhiteSpace(commit) ? null : commit,
				HttpContext.RequestAborted);
			return FromResult(result);
		}

		return ApiError(400, "Unknown release-creation method. Provide a file (multipart) or method=git (json).");
	}

	/// <summary>删除发布。</summary>
	[HttpDelete("/api/packages/{author}/{name}/releases/{id:int}/")]
	public async Task<IActionResult> Delete(string author, string name, int id)
	{
		var (user, err) = await RequireUserAsync();
		if (err is not null) return err;

		var pkg = await _packages.FindAsync(author, name, HttpContext.RequestAborted);
		if (pkg is null) return ApiError(404, "Package not found");

		var result = await _releases.DeleteReleaseAsync(user!, pkg, id, HttpContext.RequestAborted);
		return FromResult(result);
	}

	/// <summary>审核通过发布。</summary>
	[HttpPost("/api/packages/{author}/{name}/releases/{id:int}/approve/")]
	public async Task<IActionResult> Approve(string author, string name, int id)
	{
		var (user, err) = await RequireUserAsync();
		if (err is not null) return err;

		var pkg = await _packages.FindAsync(author, name, HttpContext.RequestAborted);
		if (pkg is null) return ApiError(404, "Package not found");

		var result = await _releases.ApproveReleaseAsync(user!, pkg, id, HttpContext.RequestAborted);
		return FromResult(result);
	}

	public sealed record GitReleaseBody(string? Method, string Ref, string? Name, string? Title, string? ReleaseNotes);
}
