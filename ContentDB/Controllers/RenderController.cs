// ContentDB C# —— Markdown / hypertext 渲染端点
// 对应原 /api/markdown/(POST 原始 markdown 文本)与 /api/hypertext/(POST + formspec_version)。
// 本地渲染,无需回源。

using ContentDB.Core.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace ContentDB.Api.Controllers;

[ApiController]
public sealed class RenderController : ControllerBase
{
	private readonly IMarkdownService _markdown;

	public RenderController(IMarkdownService markdown) => _markdown = markdown;

	/// <summary>POST body 为 markdown 文本,返回渲染后的 HTML。</summary>
	[HttpPost("/api/markdown/")]
	[Consumes("text/plain", "text/markdown", "application/octet-stream")]
	public async Task<IActionResult> Markdown()
	{
		using var reader = new StreamReader(Request.Body);
		var body = await reader.ReadToEndAsync(HttpContext.RequestAborted);
		var html = _markdown.RenderHtml(body);
		return Content(html, "text/html");
	}

	/// <summary>POST body 为 markdown/html,?formspec_version= 必填,返回 hypertext JSON 字符串。</summary>
	[HttpPost("/api/hypertext/")]
	public async Task<IActionResult> Hypertext()
	{
		if (!int.TryParse(Request.Query["formspec_version"], out var fsVersion))
			return BadRequest(new { error = "formspec_version is required" });

		var includeImages = Request.Query["include_images"].ToString() is not ("false" or "0" or "no");

		using var reader = new StreamReader(Request.Body);
		var body = await reader.ReadToEndAsync(HttpContext.RequestAborted);

		var isMarkdown = Request.ContentType?.Contains("markdown", StringComparison.OrdinalIgnoreCase) ?? false;
		var hypertext = isMarkdown
			? _markdown.RenderHypertext(body, fsVersion, includeImages)
			: _markdown.RenderHypertext(body, fsVersion, includeImages); // body 为 html 时也走同一转换(内部会清理标签)

		return new JsonResult(hypertext);
	}
}
