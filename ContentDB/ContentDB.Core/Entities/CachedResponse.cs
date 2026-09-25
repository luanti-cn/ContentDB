// ContentDB C# Mirror
// 缓存的上游 API JSON 响应。以 请求键(方法+路径+查询+语言) 为主键。

namespace ContentDB.Core.Entities;

public class CachedResponse
{
	/// <summary>缓存键,例如 "GET:/api/packages/?type=mod|lang=zh"。</summary>
	public string CacheKey { get; set; } = "";

	/// <summary>原始 JSON 响应体(已按镜像 PublicBaseUrl 改写 URL)。</summary>
	public string Body { get; set; } = "";

	public string? ContentType { get; set; }

	public int StatusCode { get; set; } = 200;

	/// <summary>缓存写入时间(UTC)。</summary>
	public DateTimeOffset FetchedAt { get; set; }

	/// <summary>过期时间(UTC)。超过即视为陈旧,需要回源刷新。</summary>
	public DateTimeOffset ExpiresAt { get; set; }

	public bool IsFresh(DateTimeOffset now) => now < ExpiresAt;
}
