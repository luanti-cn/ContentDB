// ContentDB C# —— 发布 / 截图 / 更新配置 / 每日统计

namespace ContentDB.Core.Domain;

public class PackageRelease
{
	public int Id { get; set; }

	public int PackageId { get; set; }
	public Package Package { get; set; } = null!;

	public string Name { get; set; } = "";
	public string Title { get; set; } = "";
	public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

	/// <summary>下载 URL。本地文件为 "/uploads/&lt;random&gt;.zip"。</summary>
	public string Url { get; set; } = "";

	public ReleaseState State { get; set; } = ReleaseState.PROCESSING;

	public string? TaskId { get; set; }
	public string? CommitHash { get; set; }
	public int Downloads { get; set; }
	public string? ReleaseNotes { get; set; }
	public long FileSizeBytes { get; set; }

	public bool? UsesInsecureEnv { get; set; }
	public bool? UsesHttpApi { get; set; }

	public int? MinRelId { get; set; }
	public LuantiRelease? MinRel { get; set; }
	public int? MaxRelId { get; set; }
	public LuantiRelease? MaxRel { get; set; }

	public bool Approved => State == ReleaseState.APPROVED;

	/// <summary>本地文件相对路径(去掉 /uploads/ 前缀即对象存储 key)。</summary>
	public string? GetObjectKey()
		=> Url.StartsWith("/uploads/", StringComparison.Ordinal) ? Url.TrimStart('/') : null;

	public bool CheckPerm(User? user, Permission perm)
	{
		if (user is null || user.IsBanned) return false;

		bool isMaintainer = user.Id == Package.AuthorId
			|| Package.Maintainers.Any(m => m.Id == user.Id);

		return perm switch
		{
			Permission.DELETE_RELEASE => CheckDeleteRelease(user, isMaintainer),
			Permission.APPROVE_RELEASE => isMaintainer || user.Rank.AtLeast(UserRank.APPROVER),
			_ => throw new InvalidOperationException($"Permission {perm} is not related to releases"),
		};
	}

	private bool CheckDeleteRelease(User user, bool isMaintainer)
	{
		if (user.Rank.AtLeast(UserRank.ADMIN)) return true;
		if (!(isMaintainer || user.Rank.AtLeast(UserRank.EDITOR))) return false;
		if (!Package.Approved || TaskId is not null) return true;
		// 只有存在更新的发布时才允许删除较旧的
		return Package.Releases.Any(r => r.Id > Id);
	}
}

public class PackageScreenshot
{
	public const int HardMinWidth = 920;
	public const int HardMinHeight = 517;

	public int Id { get; set; }

	public int PackageId { get; set; }
	public Package Package { get; set; } = null!;

	public int Order { get; set; }
	public string Title { get; set; } = "";
	public string Url { get; set; } = "";
	public bool Approved { get; set; }
	public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

	public int Width { get; set; }
	public int Height { get; set; }
	public long FileSizeBytes { get; set; }

	public string? GetObjectKey()
		=> Url.StartsWith("/uploads/", StringComparison.Ordinal) ? Url.TrimStart('/') : null;

	/// <summary>缩略图 URL: /uploads/x.png -> /thumbnails/{level}/x.{format}</summary>
	public string GetThumbUrl(int level = 2, string format = "webp")
	{
		var url = Url.Replace("/uploads/", $"/thumbnails/{level}/");
		if (!string.IsNullOrEmpty(format))
		{
			var dot = url.LastIndexOf('.');
			if (dot >= 0) url = url[..dot] + "." + format;
		}
		return url;
	}
}

public class PackageUpdateConfig
{
	public int PackageId { get; set; }
	public Package Package { get; set; } = null!;

	public string? LastCommit { get; set; }
	public string? LastTag { get; set; }
	public DateTimeOffset? OutdatedAt { get; set; }
	public DateTimeOffset? LastCheckedAt { get; set; }
	public string? TaskId { get; set; }

	public PackageUpdateTrigger Trigger { get; set; } = PackageUpdateTrigger.COMMIT;
	public string? Ref { get; set; }
	public bool MakeRelease { get; set; }
	public bool AutoCreated { get; set; }

	public string? GetRef() => LastTag ?? LastCommit;
}

public class PackageDailyStats
{
	public int PackageId { get; set; }
	public Package Package { get; set; } = null!;
	public DateOnly Date { get; set; }

	public int PlatformMinetest { get; set; }
	public int PlatformOther { get; set; }
	public int ReasonNew { get; set; }
	public int ReasonDependency { get; set; }
	public int ReasonUpdate { get; set; }
	public int DownloadsV510 { get; set; }
	public int ViewsLuanti { get; set; }
}
