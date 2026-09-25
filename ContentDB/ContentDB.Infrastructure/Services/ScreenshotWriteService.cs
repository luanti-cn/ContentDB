// ContentDB C# —— 截图写服务实现

using ContentDB.Core.Abstractions;
using ContentDB.Core.Domain;
using ContentDB.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ContentDB.Infrastructure.Services;

public sealed class ScreenshotWriteService : IScreenshotWriteService
{
	private const long MaxImageBytes = 10L * 1024 * 1024;
	private static readonly char[] RandomChars = "abcdefghijklmnopqrstuvwxyz0123456789".ToCharArray();

	private readonly AppDbContext _db;
	private readonly IObjectStorage _storage;
	private readonly ILogger<ScreenshotWriteService> _logger;

	public ScreenshotWriteService(AppDbContext db, IObjectStorage storage, ILogger<ScreenshotWriteService> logger)
	{
		_db = db;
		_storage = storage;
		_logger = logger;
	}

	public async Task<ServiceResult> CreateAsync(User actor, Package package, string title, Stream image, long length,
		string fileName, bool isCoverImage, CancellationToken ct = default)
	{
		if (!package.CheckPerm(actor, Permission.ADD_SCREENSHOTS))
			return ServiceResult.Fail(403, "You do not have permission to add screenshots");

		var ext = GuessImageExtension(fileName);
		if (ext is null)
			return ServiceResult.Fail(400, "Unsupported image type (jpg/png/webp only)");

		if (length > MaxImageBytes)
			return ServiceResult.Fail(413, "Image too large");

		var key = $"uploads/{RandomString(10)}.{ext}";
		long size;
		using (var buffer = new MemoryStream())
		{
			await image.CopyToAsync(buffer, ct);
			size = buffer.Length;
			buffer.Position = 0;
			await _storage.PutAsync(key, buffer, MimeForExt(ext), ct);
		}

		var maxOrder = await _db.Screenshots.Where(s => s.PackageId == package.Id)
			.Select(s => (int?)s.Order).MaxAsync(ct) ?? 0;

		var ss = new PackageScreenshot
		{
			PackageId = package.Id,
			Title = title,
			Url = "/" + key,
			Order = maxOrder + 1,
			Approved = package.CheckPerm(actor, Permission.APPROVE_SCREENSHOT),
			FileSizeBytes = size,
			CreatedAt = DateTimeOffset.UtcNow,
		};
		_db.Screenshots.Add(ss);
		await _db.SaveChangesAsync(ct);

		if (isCoverImage)
		{
			package.CoverImageId = ss.Id;
			await _db.SaveChangesAsync(ct);
		}

		return ServiceResult.Ok(new { id = ss.Id, url = ss.Url });
	}

	public async Task<ServiceResult> DeleteAsync(User actor, Package package, int screenshotId, CancellationToken ct = default)
	{
		if (!package.CheckPerm(actor, Permission.ADD_SCREENSHOTS))
			return ServiceResult.Fail(403, "You do not have permission to delete screenshots");

		var ss = await _db.Screenshots.FirstOrDefaultAsync(s => s.Id == screenshotId && s.PackageId == package.Id, ct);
		if (ss is null) return ServiceResult.Fail(404, "Screenshot not found");

		if (package.CoverImageId == ss.Id) package.CoverImageId = null;
		_db.Screenshots.Remove(ss);
		await _db.SaveChangesAsync(ct);

		var key = ss.GetObjectKey();
		if (key is not null)
			try { await _storage.DeleteAsync(key, ct); } catch (Exception ex) { _logger.LogWarning(ex, "删除截图对象失败"); }

		return ServiceResult.Ok(new { success = true });
	}

	public async Task<ServiceResult> ReorderAsync(User actor, Package package, IReadOnlyList<int> orderedIds, CancellationToken ct = default)
	{
		if (!package.CheckPerm(actor, Permission.ADD_SCREENSHOTS))
			return ServiceResult.Fail(403, "No permission");

		var shots = await _db.Screenshots.Where(s => s.PackageId == package.Id).ToListAsync(ct);
		for (int i = 0; i < orderedIds.Count; i++)
		{
			var s = shots.FirstOrDefault(x => x.Id == orderedIds[i]);
			if (s is not null) s.Order = i + 1;
		}
		await _db.SaveChangesAsync(ct);
		return ServiceResult.Ok(new { success = true });
	}

	public async Task<ServiceResult> SetCoverImageAsync(User actor, Package package, int screenshotId, CancellationToken ct = default)
	{
		if (!package.CheckPerm(actor, Permission.ADD_SCREENSHOTS))
			return ServiceResult.Fail(403, "No permission");

		var exists = await _db.Screenshots.AnyAsync(s => s.Id == screenshotId && s.PackageId == package.Id, ct);
		if (!exists) return ServiceResult.Fail(404, "Screenshot not found");

		package.CoverImageId = screenshotId;
		await _db.SaveChangesAsync(ct);
		return ServiceResult.Ok(new { success = true });
	}

	private static string? GuessImageExtension(string fileName)
	{
		var lower = fileName.ToLowerInvariant();
		if (lower.EndsWith(".png")) return "png";
		if (lower.EndsWith(".jpg") || lower.EndsWith(".jpeg")) return "jpg";
		if (lower.EndsWith(".webp")) return "webp";
		return null;
	}

	private static string MimeForExt(string ext) => ext switch
	{
		"png" => "image/png",
		"jpg" => "image/jpeg",
		"webp" => "image/webp",
		_ => "application/octet-stream",
	};

	private static string RandomString(int len)
	{
		var chars = new char[len];
		var rnd = Random.Shared;
		for (int i = 0; i < len; i++) chars[i] = RandomChars[rnd.Next(RandomChars.Length)];
		return new string(chars);
	}
}
