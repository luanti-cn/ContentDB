// ContentDB C# Mirror
// 已镜像到对象存储的文件记录(release zip / 截图)。
// UpstreamPath 是官方的 /uploads/<random>.<ext>,同时直接作为 S3 对象 key(去掉前导斜杠)。

namespace ContentDB.Core.Entities;

public class MirroredFile
{
	/// <summary>官方文件路径,如 "/uploads/abc123.zip"。作为主键。</summary>
	public string UpstreamPath { get; set; } = "";

	/// <summary>对象存储中的 key(通常为 UpstreamPath 去掉前导 '/')。</summary>
	public string ObjectKey { get; set; } = "";

	public long SizeBytes { get; set; }

	public string? ContentType { get; set; }

	/// <summary>是否已成功拉取入对象存储。</summary>
	public bool IsStored { get; set; }

	public DateTimeOffset FirstSeenAt { get; set; }

	public DateTimeOffset? StoredAt { get; set; }

	/// <summary>本地记录的下载计数(镜像侧统计,不回传官方)。</summary>
	public long Downloads { get; set; }
}
