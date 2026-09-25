// ContentDB C# —— Markdown / Luanti hypertext 渲染实现
// Markdown 用 Markdig 转 HTML;再把常见 HTML 结构转成 Luanti hypertext[] 标记。
// hypertext 标记参考:<b> <i> <u> <big> <bigger> <mono> <action> <img> <global> 等。
// 这是原 Python html_to_luanti 的精简移植,覆盖标题/段落/加粗/斜体/代码/链接/列表/图片。

using System.Text;
using System.Text.RegularExpressions;
using ContentDB.Core.Abstractions;
using Markdig;

namespace ContentDB.Infrastructure.Services;

public sealed partial class MarkdownService : IMarkdownService
{
	private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
		.UseAdvancedExtensions()
		.DisableHtml()          // 禁止内联原始 HTML,防注入
		.Build();

	public string RenderHtml(string markdown)
	{
		if (string.IsNullOrEmpty(markdown)) return "";
		return Markdown.ToHtml(markdown, Pipeline);
	}

	public string RenderHypertext(string markdown, int formspecVersion, bool includeImages)
	{
		if (string.IsNullOrEmpty(markdown)) return "";

		// 先转 HTML,再从 HTML 粗粒度映射到 hypertext。
		var html = Markdown.ToHtml(markdown, Pipeline);
		return HtmlToHypertext(html, formspecVersion, includeImages);
	}

	// ---- HTML -> Luanti hypertext(基于正则的轻量转换)----

	[GeneratedRegex(@"<h[1-6][^>]*>(.*?)</h[1-6]>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
	private static partial Regex HeadingRegex();

	[GeneratedRegex(@"<(strong|b)>(.*?)</\1>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
	private static partial Regex BoldRegex();

	[GeneratedRegex(@"<(em|i)>(.*?)</\1>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
	private static partial Regex ItalicRegex();

	[GeneratedRegex(@"<code>(.*?)</code>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
	private static partial Regex CodeRegex();

	[GeneratedRegex("<a\\s+[^>]*href=\"([^\"]*)\"[^>]*>(.*?)</a>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
	private static partial Regex LinkRegex();

	[GeneratedRegex(@"<li[^>]*>(.*?)</li>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
	private static partial Regex ListItemRegex();

	[GeneratedRegex(@"<p[^>]*>(.*?)</p>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
	private static partial Regex ParagraphRegex();

	[GeneratedRegex("<img\\s+[^>]*src=\"([^\"]*)\"[^>]*>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
	private static partial Regex ImageRegex();

	[GeneratedRegex(@"<[^>]+>")]
	private static partial Regex AnyTagRegex();

	private static string HtmlToHypertext(string html, int formspecVersion, bool includeImages)
	{
		var s = html;

		// 图片:formspec >= 7 支持 <img>;否则移除
		if (includeImages && formspecVersion >= 7)
			s = ImageRegex().Replace(s, m => $"<img name={EscapeAttr(m.Groups[1].Value)} width=128 height=128>");
		else
			s = ImageRegex().Replace(s, "");

		// 标题 -> 加粗大字 + 换行
		s = HeadingRegex().Replace(s, m => $"\n<big><b>{Inner(m.Groups[1].Value)}</b></big>\n");

		// 链接 -> action 标签(formspec >= 5)或纯文本
		if (formspecVersion >= 5)
			s = LinkRegex().Replace(s, m =>
			{
				var url = m.Groups[1].Value;
				var text = Inner(m.Groups[2].Value);
				return $"<action name={EscapeAttr(url)}><style color=#5599ff>{text}</style></action>";
			});
		else
			s = LinkRegex().Replace(s, m => Inner(m.Groups[2].Value));

		s = BoldRegex().Replace(s, m => $"<b>{Inner(m.Groups[2].Value)}</b>");
		s = ItalicRegex().Replace(s, m => $"<i>{Inner(m.Groups[2].Value)}</i>");
		s = CodeRegex().Replace(s, m => $"<mono>{Inner(m.Groups[1].Value)}</mono>");

		// 列表项 -> "• ..."
		s = ListItemRegex().Replace(s, m => $"\n• {Inner(m.Groups[1].Value)}");
		s = Regex.Replace(s, @"</?(ul|ol)[^>]*>", "\n", RegexOptions.IgnoreCase);

		// 段落 -> 文本 + 双换行
		s = ParagraphRegex().Replace(s, m => $"{Inner(m.Groups[1].Value)}\n\n");

		// <br>
		s = Regex.Replace(s, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);

		// 去掉剩余标签
		s = AnyTagRegex().Replace(s, "");

		s = System.Net.WebUtility.HtmlDecode(s);

		// 合并多余空行
		s = Regex.Replace(s, @"\n{3,}", "\n\n").Trim();

		return s;
	}

	private static string Inner(string htmlFragment)
	{
		// 递归清理嵌套标签为纯文本(hypertext 内不再嵌套复杂结构)
		var t = AnyTagRegex().Replace(htmlFragment, "");
		return System.Net.WebUtility.HtmlDecode(t);
	}

	private static string EscapeAttr(string value)
		=> value.Replace("\\", "\\\\").Replace(";", "\\;");
}
