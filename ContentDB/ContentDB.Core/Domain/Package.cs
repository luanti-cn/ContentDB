// ContentDB C# —— Package 实体(核心)
// 从原 Python Package 模型移植,含权限判定 CheckPerm。

namespace ContentDB.Core.Domain;

public class Package
{
	public int Id { get; set; }

	public int AuthorId { get; set; }
	public User Author { get; set; } = null!;

	/// <summary>技术名(^[a-z0-9_]+$,每作者唯一)。</summary>
	public string Name { get; set; } = "";
	public string Title { get; set; } = "";
	public string ShortDesc { get; set; } = "";
	public string? Desc { get; set; }

	public PackageType Type { get; set; }
	public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
	public DateTimeOffset? ApprovedAt { get; set; }

	public int LicenseId { get; set; } = 1;
	public License License { get; set; } = null!;
	public int MediaLicenseId { get; set; } = 1;
	public License MediaLicense { get; set; } = null!;

	public PackageState State { get; set; } = PackageState.WIP;
	public PackageDevState? DevState { get; set; }
	public PackageAIDisclosure AiDisclosure { get; set; } = PackageAIDisclosure.UNKNOWN;

	public double Score { get; set; }
	public double ScoreDownloads { get; set; }
	public int Downloads { get; set; }

	public int? ReviewThreadId { get; set; }
	public Thread? ReviewThread { get; set; }

	public bool SupportsAllGames { get; set; }
	public bool SensitivePackage { get; set; }

	// 链接
	public string? Repo { get; set; }
	public string? Website { get; set; }
	public string? IssueTracker { get; set; }
	public int? Forums { get; set; }
	public string? VideoUrl { get; set; }
	public string? DonateUrl { get; set; }
	public string? TranslationUrl { get; set; }

	public string? InsecureEnvJustification { get; set; }
	public string? HttpApiJustification { get; set; }

	public bool EnableGameSupportDetection { get; set; } = true;

	public int? CoverImageId { get; set; }
	public PackageScreenshot? CoverImage { get; set; }

	// ---- 关系 ----
	public ICollection<PackageRelease> Releases { get; set; } = new List<PackageRelease>();
	public ICollection<PackageScreenshot> Screenshots { get; set; } = new List<PackageScreenshot>();
	public ICollection<Dependency> Dependencies { get; set; } = new List<Dependency>();
	public ICollection<MetaPackage> Provides { get; set; } = new List<MetaPackage>();
	public ICollection<Tag> Tags { get; set; } = new List<Tag>();
	public ICollection<ContentWarning> ContentWarnings { get; set; } = new List<ContentWarning>();
	public ICollection<User> Maintainers { get; set; } = new List<User>();
	public ICollection<PackageGameSupport> SupportedGames { get; set; } = new List<PackageGameSupport>();
	public ICollection<Thread> Threads { get; set; } = new List<Thread>();
	public ICollection<PackageReview> Reviews { get; set; } = new List<PackageReview>();
	public ICollection<PackageAlias> Aliases { get; set; } = new List<PackageAlias>();
	public ICollection<PackageTranslation> Translations { get; set; } = new List<PackageTranslation>();
	public PackageUpdateConfig? UpdateConfig { get; set; }

	public bool Approved => State == PackageState.APPROVED;

	public string GetId() => $"{Author.Username}/{Name}";

	public string NormalisedName => Name.EndsWith("_game") ? Name[..^5] : Name;

	/// <summary>包权限判定(移植自原 Package.check_perm)。</summary>
	public bool CheckPerm(User? user, Permission perm)
	{
		if (perm == Permission.VIEW_PACKAGE)
			return State == PackageState.APPROVED || CheckPerm(user, Permission.EDIT_PACKAGE);

		if (user is null || user.IsBanned) return false;

		bool isOwner = user.Id == AuthorId;
		bool isMaintainer = isOwner || user.Rank.AtLeast(UserRank.EDITOR)
			|| Maintainers.Any(m => m.Id == user.Id);
		bool isApprover = user.Rank.AtLeast(UserRank.APPROVER);

		return perm switch
		{
			Permission.CREATE_THREAD => user.Rank.AtLeast(UserRank.NEW_MEMBER),
			Permission.MAKE_RELEASE or Permission.ADD_SCREENSHOTS => isMaintainer,
			Permission.EDIT_PACKAGE => isMaintainer && user.Rank.AtLeast(UserRank.NEW_MEMBER),
			Permission.APPROVE_RELEASE => isMaintainer || isApprover,
			Permission.CHANGE_NAME => !Approved,
			Permission.APPROVE_NEW or Permission.CHANGE_AUTHOR => isApprover,
			Permission.APPROVE_SCREENSHOT => (isMaintainer || isApprover)
				&& user.Rank.AtLeast(Approved ? UserRank.TRUSTED_MEMBER : UserRank.NEW_MEMBER),
			Permission.EDIT_MAINTAINERS or Permission.DELETE_PACKAGE
				=> isOwner || user.Rank.AtLeast(UserRank.EDITOR),
			Permission.UNAPPROVE_PACKAGE => isOwner || user.Rank.AtLeast(UserRank.APPROVER),
			Permission.CHANGE_RELEASE_URL => user.Rank.AtLeast(UserRank.MODERATOR),
			_ => throw new InvalidOperationException($"Permission {perm} is not related to packages"),
		};
	}
}

public class Dependency
{
	public int Id { get; set; }

	public int DependerId { get; set; }
	public Package Depender { get; set; } = null!;

	public int? PackageId { get; set; }
	public Package? Package { get; set; }

	public int? MetaPackageId { get; set; }
	public MetaPackage? MetaPackage { get; set; }

	public bool Optional { get; set; }

	public string GetName() => MetaPackage?.Name ?? Package?.Name
		?? throw new InvalidOperationException("Malformed dependency");
}

public class PackageGameSupport
{
	public int Id { get; set; }

	public int PackageId { get; set; }
	public Package Package { get; set; } = null!;

	public int GameId { get; set; }
	public Package Game { get; set; } = null!;

	public bool Supports { get; set; } = true;
	public int Confidence { get; set; } = 1;
}

public class PackageTranslation
{
	public int PackageId { get; set; }
	public Package Package { get; set; } = null!;

	public string LanguageId { get; set; } = "";
	public Language Language { get; set; } = null!;

	public string? Title { get; set; }
	public string? ShortDesc { get; set; }
	public string? Desc { get; set; }
}

public class PackageAlias
{
	public int Id { get; set; }

	public int PackageId { get; set; }
	public Package Package { get; set; } = null!;

	public string Author { get; set; } = "";
	public string Name { get; set; } = "";

	public string AsDict() => $"{Author}/{Name}";
}
