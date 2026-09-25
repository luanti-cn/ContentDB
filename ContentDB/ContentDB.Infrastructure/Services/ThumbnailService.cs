// ContentDB C# —— 缩略图生成服务实现(ImageSharp)
// 源图:对象存储 uploads/<file>;输出:thumbnails/{level}/<file>.<format>,缓存回对象存储。
// 档位与原 ContentDB 对齐:1=100x67, 2=270x180, 3=350x233, 4=1100x520(等比裁剪填充)。

using ContentDB.Core.Abstractions;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace ContentDB.Infrastructure.Services;

public sealed class ThumbnailService : IThumbnailService
{
	// level -> (width, height),与原站 ALLOWED_RESOLUTIONS 一致
	private static readonly Dictionary<int, (int W, int H)> Levels = new()
	{
		[1] = (100, 67),
		[2] = (270, 180),
		[3] = (350, 233),
		[4] = (1100, 520),
	};

	private static readonly HashSet<string> AllowedFormats = new(StringComparer.OrdinalIgnoreCase)
	{
		"png", "webp", "jpg", "jpeg"
	};

	private readonly IObjectStorage _storage;
	private readonly ILogger<ThumbnailService> _logger;

	public ThumbnailService(IObjectStorage storage, ILogger<ThumbnailService> logger)
	{
		_storage = storage;
		_logger = logger;
	}

	public async Task<ThumbnailResult> GetOrCreateAsync(int level, string sourceKey, string format, CancellationToken ct = default)
	{
		if (!Levels.TryGetValue(level, out var size))
			return new ThumbnailResult(false, null, null, 400);
		if (!AllowedFormats.Contains(format))
			format = "webp";

		var normalizedFormat = format.Equals("jpeg", StringComparison.OrdinalIgnoreCase) ? "jpg" : format.ToLowerInvariant();

		// 缓存 key:thumbnails/{level}/<source-without-ext>.<format>
		var srcNoExt = StripExtension(sourceKey);
		var thumbKey = $"thumbnails/{level}/{srcNoExt}.{normalizedFormat}";

		// 命中缓存
		var cached = await _storage.OpenReadAsync(thumbKey, ct);
		if (cached is not null)
			return new ThumbnailResult(true, cached, MimeFor(normalizedFormat), 200);

		// 读源图(uploads/<file>)
		var srcStream = await _storage.OpenReadAsync(sourceKey, ct);
		if (srcStream is null)
			return new ThumbnailResult(false, null, null, 404);

		try
		{
			byte[] output;
			await using (srcStream)
			using (var image = await Image.LoadAsync(srcStream, ct))
			{
				// 等比裁剪填充到目标尺寸(居中裁剪)
				image.Mutate(x => x.Resize(new ResizeOptions
				{
					Size = new Size(size.W, size.H),
					Mode = ResizeMode.Crop,
					Position = AnchorPositionMode.Center,
				}));

				await using var ms = new MemoryStream();
				await SaveAsync(image, ms, normalizedFormat, ct);
				output = ms.ToArray();
			}

			// 写回对象存储缓存(失败不阻塞交付)
			try
			{
				await using var up = new MemoryStream(output, writable: false);
				await _storage.PutAsync(thumbKey, up, MimeFor(normalizedFormat), ct);
			}
			catch (Exception ex)
			{
				_logger.LogWarning(ex, "缩略图缓存写入失败 {Key}", thumbKey);
			}

			return new ThumbnailResult(true, new MemoryStream(output, writable: false), MimeFor(normalizedFormat), 200);
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "缩略图生成失败 {Source} level={Level}", sourceKey, level);
			return new ThumbnailResult(false, null, null, 500);
		}
	}

	private static async Task SaveAsync(Image image, Stream target, string format, CancellationToken ct)
	{
		switch (format)
		{
			case "png":
				await image.SaveAsync(target, new PngEncoder(), ct); break;
			case "jpg":
				await image.SaveAsync(target, new JpegEncoder { Quality = 85 }, ct); break;
			default:
				await image.SaveAsync(target, new WebpEncoder { Quality = 85 }, ct); break;
		}
	}

	private static string MimeFor(string format) => format switch
	{
		"png" => "image/png",
		"jpg" => "image/jpeg",
		_ => "image/webp",
	};

	private static string StripExtension(string key)
	{
		var dot = key.LastIndexOf('.');
		var slash = key.LastIndexOf('/');
		return dot > slash ? key[..dot] : key;
	}
}
