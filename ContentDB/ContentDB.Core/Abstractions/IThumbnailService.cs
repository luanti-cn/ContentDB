// ContentDB C# —— 缩略图生成服务抽象
// 对应原 Python 的 thumbnails blueprint:按 level 生成裁剪后的多分辨率缩略图。

namespace ContentDB.Core.Abstractions;

public sealed record ThumbnailResult(bool Success, Stream? Content, string? ContentType, int StatusCode);

public interface IThumbnailService
{
	/// <summary>
	/// 生成/取用某源图的缩略图。首次生成后缓存到对象存储(thumbnails/{level}/...);后续直接命中。
	/// </summary>
	/// <param name="level">尺寸档位 1..4(对应 100x67 / 270x180 / 350x233 / 1100x520)。</param>
	/// <param name="path">相对图片路径,如 "1/abcd.png"(level 已在路由中,path 为 level 之后部分)。</param>
	/// <param name="format">输出格式:png / webp / jpg。</param>
	Task<ThumbnailResult> GetOrCreateAsync(int level, string sourceKey, string format, CancellationToken ct = default);
}
