// ContentDB C# —— 领域枚举
// 从原 Python 模型忠实移植。枚举名与官方 JSON 输出保持一致(用 .ToName() 输出小写名)。

namespace ContentDB.Core.Domain;

/// <summary>包类型。</summary>
public enum PackageType
{
	MOD,
	GAME,
	TXP,
}

/// <summary>包开发状态。</summary>
public enum PackageDevState
{
	WIP,
	BETA,
	ACTIVELY_DEVELOPED,
	MAINTENANCE_ONLY,
	AS_IS,
	DEPRECATED,
	LOOKING_FOR_MAINTAINER,
}

/// <summary>包审核状态。</summary>
public enum PackageState
{
	WIP,
	CHANGES_NEEDED,
	READY_FOR_REVIEW,
	APPROVED,
	DELETED,
}

/// <summary>AI 披露。</summary>
public enum PackageAIDisclosure
{
	UNKNOWN,
	NONE,
	ASSISTED,
	GENERATED,
}

/// <summary>发布状态。</summary>
public enum ReleaseState
{
	PROCESSING,
	APPROVED,
	FAILED,
	UNAPPROVED,
	ARCHIVED,
}

/// <summary>git 自动发布触发方式。</summary>
public enum PackageUpdateTrigger
{
	COMMIT,
	TAG,
}

/// <summary>
/// 内容来源标记 —— 本站独有扩展。仅用于 **合并后的 API 输出 DTO**,不落在本地实体表上:
/// 本地领域实体只建模 LOCAL(自主运营)内容;UPSTREAM(官方)内容走独立的
/// 上游缓存表(CachedResponse/MirroredFile),在读取时于应用层合并并打上此标记。
/// </summary>
public enum ContentOrigin
{
	LOCAL,
	UPSTREAM,
}

public static class PackageTypeExtensions
{
	public static string ToName(this PackageType t) => t.ToString().ToLowerInvariant();

	public static PackageType? Parse(string? name)
	{
		if (string.IsNullOrEmpty(name)) return null;
		return name.ToUpperInvariant() switch
		{
			"MOD" => PackageType.MOD,
			"GAME" => PackageType.GAME,
			"TXP" => PackageType.TXP,
			_ => null,
		};
	}
}
