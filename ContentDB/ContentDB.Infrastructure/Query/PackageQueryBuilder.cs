// ContentDB C# —— PackageQueryBuilder(EF 依赖部分,置于 Infrastructure)
// 把 PackageQuery 条件应用到 EF IQueryable<Package>。

using ContentDB.Core.Domain;
using ContentDB.Core.Query;
using Microsoft.EntityFrameworkCore;

namespace ContentDB.Infrastructure.Query;

public sealed class PackageQueryBuilder
{
	private readonly PackageQuery _q;

	public PackageQueryBuilder(PackageQuery q) => _q = q;

	public IQueryable<Package> Apply(IQueryable<Package> query, int? gameId, int? authorId, IReadOnlyCollection<int> licenseIds)
	{
		if (_q.OnlyApproved)
			query = query.Where(p => p.State == PackageState.APPROVED);

		if (_q.Types.Count > 0)
			query = query.Where(p => _q.Types.Contains(p.Type));

		if (authorId is not null)
			query = query.Where(p => p.AuthorId == authorId);

		if (gameId is not null)
			query = query.Where(p => p.SupportedGames.Any(g => g.GameId == gameId && g.Supports));

		if (!string.IsNullOrEmpty(_q.HasLang) && _q.HasLang != "en")
			query = query.Where(p => p.Translations.Any(t => t.LanguageId == _q.HasLang));

		foreach (var tag in _q.TagNames)
			query = query.Where(p => p.Tags.Any(t => t.Name == tag));

		if (_q.HideFlags.Contains("*"))
			query = query.Where(p => !p.ContentWarnings.Any());
		else
			foreach (var flag in _q.HideFlags)
				query = query.Where(p => !p.ContentWarnings.Any(w => w.Name == flag));

		if (_q.HideFlags.Contains("genai") || _q.HideFlags.Contains("anyai"))
		{
			query = query.Where(p => p.AiDisclosure != PackageAIDisclosure.GENERATED);
			if (_q.HideFlags.Contains("anyai"))
				query = query.Where(p => p.AiDisclosure != PackageAIDisclosure.ASSISTED);
		}

		if (_q.Flags.Contains("wip"))
			query = query.Where(p => p.DevState == PackageDevState.WIP);
		if (_q.Flags.Contains("deprecated"))
			query = query.Where(p => p.DevState == PackageDevState.DEPRECATED);
		if (_q.Flags.Contains("*"))
			query = query.Where(p => p.ContentWarnings.Any());

		if (licenseIds.Count > 0)
			query = query.Where(p => licenseIds.Contains(p.LicenseId) || licenseIds.Contains(p.MediaLicenseId));

		// 全文搜索:多字段 ILIKE(name/title/short_desc/desc/author),EF.Functions.ILike 走 Postgres 原生。
		if (!string.IsNullOrWhiteSpace(_q.Search))
		{
			var pattern = "%" + _q.Search.Trim() + "%";
			query = query.Where(p =>
				EF.Functions.ILike(p.Name, pattern) ||
				EF.Functions.ILike(p.Title, pattern) ||
				EF.Functions.ILike(p.ShortDesc, pattern) ||
				(p.Desc != null && EF.Functions.ILike(p.Desc, pattern)) ||
				EF.Functions.ILike(p.Author.Username, pattern));
		}

		if (_q.HideWip)
			query = query.Where(p => p.DevState == null || p.DevState != PackageDevState.WIP);
		if (_q.HideDeprecated)
			query = query.Where(p => p.DevState == null || p.DevState != PackageDevState.DEPRECATED);

		if (_q.Version is not null)
		{
			var vid = _q.Version.Id;
			query = query.Where(p => p.Releases.Any(r =>
				(r.MinRelId == null || r.MinRelId <= vid) &&
				(r.MaxRelId == null || r.MaxRelId >= vid)));
		}
		else if (_q.NotVersion is not null)
		{
			var vid = _q.NotVersion.Id;
			query = query.Where(p => !p.Releases.Any(r =>
				(r.MinRelId == null || r.MinRelId <= vid) &&
				(r.MaxRelId == null || r.MaxRelId >= vid)));
		}

		query = ApplyOrder(query);

		if (_q.Limit is int lim)
			query = query.Take(lim);

		return query;
	}

	private IQueryable<Package> ApplyOrder(IQueryable<Package> query)
	{
		if (_q.Random)
			return query.OrderBy(_ => EF.Functions.Random());

		bool asc = _q.OrderDir == "asc";

		// 搜索且未指定排序:标题精确匹配优先,其次按分数(粗粒度相关性)。
		if (string.IsNullOrEmpty(_q.OrderBy) && !string.IsNullOrWhiteSpace(_q.Search))
		{
			var term = _q.Search.Trim();
			var titlePattern = "%" + term + "%";
			return query
				.OrderByDescending(p => EF.Functions.ILike(p.Title, term) ? 2 : (EF.Functions.ILike(p.Title, titlePattern) ? 1 : 0))
				.ThenByDescending(p => p.Score);
		}

		return _q.OrderBy switch
		{
			null or "score" => asc ? query.OrderBy(p => p.Score) : query.OrderByDescending(p => p.Score),
			"reviews" => asc
				? query.Where(p => p.Reviews.Any(r => r.Approved)).OrderBy(p => p.Score - p.ScoreDownloads)
				: query.Where(p => p.Reviews.Any(r => r.Approved)).OrderByDescending(p => p.Score - p.ScoreDownloads),
			"name" => asc ? query.OrderBy(p => p.Name) : query.OrderByDescending(p => p.Name),
			"title" => asc ? query.OrderBy(p => p.Title) : query.OrderByDescending(p => p.Title),
			"downloads" => asc ? query.OrderBy(p => p.Downloads) : query.OrderByDescending(p => p.Downloads),
			"created_at" or "date" => asc ? query.OrderBy(p => p.CreatedAt) : query.OrderByDescending(p => p.CreatedAt),
			"approved_at" => asc ? query.OrderBy(p => p.ApprovedAt) : query.OrderByDescending(p => p.ApprovedAt),
			"last_release" => asc
				? query.OrderBy(p => p.Releases.Max(r => (DateTimeOffset?)r.CreatedAt))
				: query.OrderByDescending(p => p.Releases.Max(r => (DateTimeOffset?)r.CreatedAt)),
			_ => query,
		};
	}
}
