// ContentDB C# —— Markdown / Luanti hypertext 渲染抽象

namespace ContentDB.Core.Abstractions;

public interface IMarkdownService
{
	/// <summary>Markdown -> 安全 HTML(用于 Web 展示、/api/markdown)。</summary>
	string RenderHtml(string markdown);

	/// <summary>
	/// Markdown/HTML -> Luanti hypertext[] 标记(用于游戏内 for-client 展示)。
	/// </summary>
	/// <param name="markdown">源 Markdown</param>
	/// <param name="formspecVersion">formspec 版本(影响可用标签)</param>
	/// <param name="includeImages">是否包含图片标签</param>
	string RenderHypertext(string markdown, int formspecVersion, bool includeImages);
}
