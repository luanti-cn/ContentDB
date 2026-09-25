// ContentDB C# —— 截图写端点(上传/删除/排序/封面)

using ContentDB.Api.Auth;
using ContentDB.Core.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace ContentDB.Api.Controllers;

[ApiController]
public sealed class ScreenshotWriteController : ApiControllerBase
{
	private readonly IPackageWriteService _packages;
	private readonly IScreenshotWriteService _screenshots;

	public ScreenshotWriteController(
		ICurrentUserAccessor currentUser,
		IPackageWriteService packages,
		IScreenshotWriteService screenshots)
		: base(currentUser)
	{
		_packages = packages;
		_screenshots = screenshots;
	}

	/// <summary>上传截图(multipart:file + title + is_cover_image?)。</summary>
	[HttpPost("/api/packages/{author}/{name}/screenshots/new/")]
	[RequestSizeLimit(20L * 1024 * 1024)]
	public async Task<IActionResult> Create(string author, string name)
	{
		var (user, err) = await RequireUserAsync();
		if (err is not null) return err;

		var pkg = await _packages.FindAsync(author, name, HttpContext.RequestAborted);
		if (pkg is null) return ApiError(404, "Package not found");

		if (!Request.HasFormContentType) return ApiError(400, "Expected multipart/form-data");
		var form = await Request.ReadFormAsync(HttpContext.RequestAborted);
		var file = form.Files.GetFile("file");
		if (file is null) return ApiError(400, "Missing 'file'");
		var title = form["title"].ToString();
		if (string.IsNullOrWhiteSpace(title)) return ApiError(400, "title is required");
		var isCover = form["is_cover_image"].ToString() is "true" or "1" or "yes";

		await using var stream = file.OpenReadStream();
		var result = await _screenshots.CreateAsync(
			user!, pkg, title, stream, file.Length, file.FileName, isCover, HttpContext.RequestAborted);
		return FromResult(result);
	}

	[HttpDelete("/api/packages/{author}/{name}/screenshots/{id:int}/")]
	public async Task<IActionResult> Delete(string author, string name, int id)
	{
		var (user, err) = await RequireUserAsync();
		if (err is not null) return err;
		var pkg = await _packages.FindAsync(author, name, HttpContext.RequestAborted);
		if (pkg is null) return ApiError(404, "Package not found");
		return FromResult(await _screenshots.DeleteAsync(user!, pkg, id, HttpContext.RequestAborted));
	}

	public sealed record OrderBody(List<int> Order);

	[HttpPost("/api/packages/{author}/{name}/screenshots/order/")]
	public async Task<IActionResult> Order(string author, string name, [FromBody] List<int> order)
	{
		var (user, err) = await RequireUserAsync();
		if (err is not null) return err;
		var pkg = await _packages.FindAsync(author, name, HttpContext.RequestAborted);
		if (pkg is null) return ApiError(404, "Package not found");
		if (order is null) return ApiError(400, "Expected array of screenshot ids");
		return FromResult(await _screenshots.ReorderAsync(user!, pkg, order, HttpContext.RequestAborted));
	}

	public sealed record CoverBody(int CoverImage);

	[HttpPost("/api/packages/{author}/{name}/screenshots/cover-image/")]
	public async Task<IActionResult> Cover(string author, string name, [FromBody] CoverBody body)
	{
		var (user, err) = await RequireUserAsync();
		if (err is not null) return err;
		var pkg = await _packages.FindAsync(author, name, HttpContext.RequestAborted);
		if (pkg is null) return ApiError(404, "Package not found");
		return FromResult(await _screenshots.SetCoverImageAsync(user!, pkg, body.CoverImage, HttpContext.RequestAborted));
	}
}
