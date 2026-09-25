// ContentDB C# —— 包查询参数 + QueryBuilder(本地包)
// 移植原 querybuilder.py 的过滤/排序语义(只覆盖本地 AppDbContext 查询)。

using ContentDB.Core.Domain;

namespace ContentDB.Core.Query;

/// <summary>从请求查询串解析出的包检索条件。</summary>
public sealed class PackageQuery
{
	public List<PackageType> Types { get; set; } = new();
	public string? Search { get; set; }
	public string? Author { get; set; }
	public List<string> TagNames { get; set; } = new();
	public List<string> HideFlags { get; set; } = new();   // content warnings + 特殊(*, wip, deprecated)
	public List<string> Flags { get; set; } = new();
	public List<string> LicenseNames { get; set; } = new();
	public string? GameKey { get; set; }
	public string? HasLang { get; set; }

	public bool Random { get; set; }
	public bool Lucky { get; set; }
	public int? Limit { get; set; }
	public string? OrderBy { get; set; }
	public string OrderDir { get; set; } = "desc";

	public bool HideWip { get; set; }
	public bool HideDeprecated { get; set; }

	/// <summary>解析后的引擎版本(用于发布兼容过滤)。</summary>
	public LuantiRelease? Version { get; set; }
	public LuantiRelease? NotVersion { get; set; }

	public bool OnlyApproved { get; set; } = true;

	public static PackageQuery FromDictionary(IReadOnlyDictionary<string, List<string>> args)
	{
		var q = new PackageQuery();

		if (args.TryGetValue("type", out var types))
			foreach (var t in types)
			{
				var pt = PackageTypeExtensions.Parse(t);
				if (pt is not null) q.Types.Add(pt.Value);
			}

		if (args.TryGetValue("tag", out var tags)) q.TagNames.AddRange(tags);
		if (args.TryGetValue("hide", out var hide)) q.HideFlags.AddRange(hide);
		if (args.TryGetValue("flag", out var flags)) q.Flags.AddRange(flags);
		if (args.TryGetValue("license", out var lic)) q.LicenseNames.AddRange(lic);

		q.Search = Single(args, "q");
		if (string.IsNullOrWhiteSpace(q.Search)) q.Search = null;
		q.Author = Single(args, "author");
		q.GameKey = Single(args, "game");
		q.HasLang = Single(args, "lang");
		if (q.HasLang == "") q.HasLang = null;

		q.Random = args.ContainsKey("random");
		q.Lucky = args.ContainsKey("lucky");
		q.Limit = q.Lucky ? 1 : ParseIntOrNull(Single(args, "limit"));
		q.OrderBy = Single(args, "sort");
		if (q.OrderBy == "") q.OrderBy = null;
		q.OrderDir = Single(args, "order") ?? "desc";

		// android_default / desktop_default 展开
		if (q.HideFlags.Contains("android_default"))
		{
			q.HideFlags.Remove("android_default");
			if (!q.HideFlags.Contains("*")) q.HideFlags.Add("*");
			if (!q.HideFlags.Contains("deprecated")) q.HideFlags.Add("deprecated");
		}
		if (q.HideFlags.Contains("desktop_default"))
		{
			q.HideFlags.Remove("desktop_default");
			if (!q.HideFlags.Contains("deprecated")) q.HideFlags.Add("deprecated");
		}

		q.HideWip = q.HideFlags.Contains("wip");
		q.HideDeprecated = q.HideFlags.Contains("deprecated");
		q.HideFlags.RemoveAll(f => f is "nonfree" or "wip" or "deprecated");

		return q;
	}

	private static string? Single(IReadOnlyDictionary<string, List<string>> args, string key)
		=> args.TryGetValue(key, out var v) && v.Count > 0 ? v[0] : null;

	private static int? ParseIntOrNull(string? s)
		=> int.TryParse(s, out var i) ? i : null;
}
