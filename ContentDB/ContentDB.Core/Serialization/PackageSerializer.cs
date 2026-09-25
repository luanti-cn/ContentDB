// ContentDB C# —— 包序列化(本地实体 -> 官方 JSON 契约形状)
// 输出结构对齐原 Package.as_short_dict / as_dict,附带 origin/source 标记(本站扩展)。

using System.Text.Json.Nodes;
using ContentDB.Core.Domain;

namespace ContentDB.Core.Serialization;

public sealed class PackageSerializer
{
	private readonly string _baseUrl;

	public PackageSerializer(string publicBaseUrl) => _baseUrl = publicBaseUrl.TrimEnd('/');

	/// <summary>
	/// as_short_dict 形状(用于 /api/packages/ 列表)。
	/// </summary>
	/// <param name="sourceId">来源站点 id(本站配置的 Local.Id)。</param>
	/// <param name="sourceName">来源站点显示名(Web UI 徽章)。</param>
	/// <param name="luantiLabel">Luanti 客户端简介前缀,如 [Luanti.cn];为 null 不加。</param>
	public JsonObject ToShortDict(
		Package p, int? releaseId, bool includeVcs = false,
		string sourceId = "local", string? sourceName = null, string? luantiLabel = null)
	{
		var thumb = GetThumbUrl(p, 1, "png");
		var shortDesc = p.ShortDesc;
		if (p.DevState == PackageDevState.WIP)
			shortDesc = "Work in Progress. " + p.ShortDesc;

		if (!string.IsNullOrEmpty(luantiLabel))
			shortDesc = $"{luantiLabel} {shortDesc}";

		var obj = new JsonObject
		{
			["name"] = p.Name,
			["title"] = p.Title,
			["author"] = p.Author.Username,
			["short_description"] = shortDesc,
			["type"] = p.Type.ToName(),
			["release"] = releaseId,
			["thumbnail"] = thumb is null ? null : _baseUrl + thumb,
			// 本站扩展:来源标记
			["origin"] = ContentOrigin.LOCAL.ToString().ToLowerInvariant(),
			["source"] = sourceId,
			["source_name"] = sourceName,
			// 用于跨源合并排序(ISO 时间)
			["created_at"] = p.CreatedAt.ToString("o"),
		};

		if (p.Aliases.Count > 0)
		{
			var arr = new JsonArray();
			foreach (var a in p.Aliases) arr.Add(a.AsDict());
			obj["aliases"] = arr;
		}

		if (includeVcs)
			obj["repo"] = p.Repo;

		return obj;
	}

	/// <summary>as_key_dict 形状(fmt=keys)。</summary>
	public JsonObject ToKeyDict(Package p) => new()
	{
		["name"] = p.Name,
		["author"] = p.Author.Username,
		["type"] = p.Type.ToName(),
	};

	/// <summary>
	/// as_dict 形状(用于单包详情 /api/packages/{author}/{name}/)。
	/// 需要 Package 已 Include:Author、Screenshots、Aliases、Tags、ContentWarnings、
	/// Provides、License、MediaLicense。releaseId 为该包最新(APPROVED)发布 id。
	/// </summary>
	public JsonObject ToDict(Package p, int? releaseId)
	{
		var obj = ToShortDict(p, releaseId, includeVcs: false,
			sourceId: "local", sourceName: null, luantiLabel: null);

		obj["long_description"] = p.Desc;
		obj["dev_state"] = p.DevState?.ToString();
		obj["license"] = p.License?.Name;
		obj["media_license"] = p.MediaLicense?.Name;
		obj["website"] = p.Website;
		obj["issue_tracker"] = p.IssueTracker;
		obj["forums"] = p.Forums;
		obj["forum_url"] = p.Forums is int fid ? $"https://forum.luanti.org/viewtopic.php?t={fid}" : null;
		obj["donate_url"] = p.DonateUrl;
		obj["video_url"] = p.VideoUrl;
		obj["translation_url"] = p.TranslationUrl;
		obj["score"] = p.Score;
		obj["downloads"] = p.Downloads;
		obj["state"] = p.State.ToString();

		obj["tags"] = ToStringArray(p.Tags.Select(t => t.Name));
		obj["content_warnings"] = ToStringArray(p.ContentWarnings.Select(w => w.Name));
		obj["provides"] = ToStringArray(p.Provides.Select(m => m.Name));

		var screenshots = new JsonArray();
		foreach (var s in p.Screenshots.Where(s => s.Approved).OrderBy(s => s.Order))
			screenshots.Add(_baseUrl + s.Url);
		obj["screenshots"] = screenshots;

		return obj;
	}

	/// <summary>发布序列化(用于 /api/packages/{author}/{name}/releases/)。</summary>
	public JsonObject ReleaseToDict(PackageRelease r) => new()
	{
		["id"] = r.Id,
		["name"] = r.Name,
		["title"] = r.Title,
		["release_notes"] = r.ReleaseNotes,
		["url"] = string.IsNullOrEmpty(r.Url) ? null : _baseUrl + r.Url,
		["release_date"] = r.CreatedAt.ToString("o"),
		["commit"] = r.CommitHash,
		["downloads"] = r.Downloads,
		["size"] = r.FileSizeBytes,
		["min_protocol"] = r.MinRel?.Protocol,
		["max_protocol"] = r.MaxRel?.Protocol,
	};

	/// <summary>截图序列化(用于 /api/packages/{author}/{name}/screenshots/)。</summary>
	public JsonObject ScreenshotToDict(PackageScreenshot s) => new()
	{
		["id"] = s.Id,
		["order"] = s.Order,
		["title"] = s.Title,
		["url"] = _baseUrl + s.Url,
		["width"] = s.Width,
		["height"] = s.Height,
		["approved"] = s.Approved,
		["is_cover_image"] = s.Package?.CoverImageId == s.Id,
	};

	private static JsonArray ToStringArray(IEnumerable<string> items)
	{
		var arr = new JsonArray();
		foreach (var i in items) arr.Add(i);
		return arr;
	}

	private string? GetThumbUrl(Package p, int level, string format)
	{
		var ss = p.Screenshots
			.Where(s => s.Approved)
			.OrderBy(s => s.Order)
			.FirstOrDefault();
		return ss?.GetThumbUrl(level, format);
	}
}
