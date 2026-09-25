// ContentDB C# —— 发布写服务实现

using ContentDB.Core.Abstractions;
using ContentDB.Core.Domain;
using ContentDB.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ContentDB.Infrastructure.Services;

public sealed class ReleaseWriteService : IReleaseWriteService
{
	private const long MaxZipBytes = 100L * 1024 * 1024; // 100 MB
	private static readonly char[] RandomChars = "abcdefghijklmnopqrstuvwxyz0123456789".ToCharArray();

	private readonly AppDbContext _db;
	private readonly IObjectStorage _storage;
	private readonly IZipMetadataExtractor _zipExtractor;
	private readonly ILogger<ReleaseWriteService> _logger;

	public ReleaseWriteService(
		AppDbContext db,
		IObjectStorage storage,
		IZipMetadataExtractor zipExtractor,
		ILogger<ReleaseWriteService> logger)
	{
		_db = db;
		_storage = storage;
		_zipExtractor = zipExtractor;
		_logger = logger;
	}

	public async Task<ServiceResult> CreateZipReleaseAsync(
		User actor, Package package, string name, string title,
		string? releaseNotes, Stream zipStream, long zipLength, string? commitHash,
		CancellationToken ct = default)
	{
		if (!package.CheckPerm(actor, Permission.MAKE_RELEASE))
			return ServiceResult.Fail(403, "You do not have permission to make releases");

		if (zipLength > MaxZipBytes)
			return ServiceResult.Fail(413, "Release file exceeds 100 MB limit");

		// 生成随机对象 key,与官方 /uploads/<random>.zip 约定一致
		var key = $"uploads/{package.Name}_{RandomString(10)}.zip";
		ZipMetadata? meta = null;
		using (var buffer = new MemoryStream())
		{
			await zipStream.CopyToAsync(buffer, ct);
			if (buffer.Length > MaxZipBytes)
				return ServiceResult.Fail(413, "Release file exceeds 100 MB limit");

			if (!LooksLikeZip(buffer))
				return ServiceResult.Fail(400, "Uploaded file is not a valid zip");

			// 解析 zip 元数据(mod.conf/依赖/provides)
			buffer.Position = 0;
			meta = _zipExtractor.Extract(buffer);

			buffer.Position = 0;
			await _storage.PutAsync(key, buffer, "application/zip", ct);
			zipLength = buffer.Length;
		}

		var release = new PackageRelease
		{
			PackageId = package.Id,
			Name = name,
			Title = string.IsNullOrEmpty(title) ? name : title,
			ReleaseNotes = releaseNotes,
			Url = "/" + key,
			State = ReleaseState.APPROVED, // 维护者上传直接可用;严格审核可改为 UNAPPROVED
			CommitHash = commitHash,
			FileSizeBytes = zipLength,
			CreatedAt = DateTimeOffset.UtcNow,
		};
		_db.Releases.Add(release);
		await _db.SaveChangesAsync(ct);

		// 应用 zip 解析出的 provides / 依赖(仅 MOD 类型有意义)
		if (meta is not null && meta.Error is null)
			await ApplyZipMetadataAsync(package, meta, ct);

		return ServiceResult.Ok(new { id = release.Id, url = release.Url });
	}

	/// <summary>把 zip 解析出的 provides(mod 名)与依赖同步到包(meta_package + dependency)。</summary>
	private async Task ApplyZipMetadataAsync(Package package, ZipMetadata meta, CancellationToken ct)
	{
		try
		{
			// provides:确保 MetaPackage 存在并关联
			foreach (var name in meta.Provides.Distinct(StringComparer.OrdinalIgnoreCase))
			{
				var mp = await GetOrCreateMetaPackageAsync(name, ct);
				if (!package.Provides.Any(p => p.Id == mp.Id))
					package.Provides.Add(mp);
			}

			// 重建依赖:先清掉本包旧的自动依赖,再按 zip 写入
			var existing = await _db.Dependencies.Where(d => d.DependerId == package.Id).ToListAsync(ct);
			_db.Dependencies.RemoveRange(existing);

			async Task AddDeps(IEnumerable<string> names, bool optional)
			{
				foreach (var name in names.Distinct(StringComparer.OrdinalIgnoreCase))
				{
					var mp = await GetOrCreateMetaPackageAsync(name, ct);
					_db.Dependencies.Add(new Dependency
					{
						DependerId = package.Id,
						MetaPackageId = mp.Id,
						Optional = optional,
					});
				}
			}

			await AddDeps(meta.Depends, optional: false);
			await AddDeps(meta.OptionalDepends, optional: true);

			await _db.SaveChangesAsync(ct);
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "应用 zip 元数据失败 package={Pkg}", package.GetId());
		}
	}

	private async Task<MetaPackage> GetOrCreateMetaPackageAsync(string name, CancellationToken ct)
	{
		var mp = await _db.MetaPackages.FirstOrDefaultAsync(m => m.Name == name, ct);
		if (mp is null)
		{
			mp = new MetaPackage { Name = name };
			_db.MetaPackages.Add(mp);
			await _db.SaveChangesAsync(ct);
		}
		return mp;
	}

	public async Task<ServiceResult> CreateVcsReleaseAsync(
		User actor, Package package, string name, string title,
		string? releaseNotes, string gitRef, CancellationToken ct = default)
	{
		if (!package.CheckPerm(actor, Permission.MAKE_RELEASE))
			return ServiceResult.Fail(403, "You do not have permission to make releases");

		if (string.IsNullOrEmpty(package.Repo))
			return ServiceResult.Fail(400, "Package has no Git repository configured");

		// 先建 PROCESSING 记录,由后台 git 导入任务填充(Phase 5)。
		var taskId = Guid.NewGuid().ToString();
		var release = new PackageRelease
		{
			PackageId = package.Id,
			Name = name,
			Title = string.IsNullOrEmpty(title) ? name : title,
			ReleaseNotes = releaseNotes,
			CommitHash = gitRef,
			State = ReleaseState.PROCESSING,
			TaskId = taskId,
			CreatedAt = DateTimeOffset.UtcNow,
		};
		_db.Releases.Add(release);
		await _db.SaveChangesAsync(ct);

		_logger.LogInformation("已创建 git 发布任务 {TaskId}(package={Pkg}, ref={Ref}),等待后台打包", taskId, package.GetId(), gitRef);
		return ServiceResult.Ok(new { id = release.Id, task_id = taskId, state = "processing" });
	}

	public async Task<ServiceResult> DeleteReleaseAsync(User actor, Package package, int releaseId, CancellationToken ct = default)
	{
		var release = await _db.Releases.Include(r => r.Package).ThenInclude(p => p.Maintainers)
			.FirstOrDefaultAsync(r => r.Id == releaseId && r.PackageId == package.Id, ct);
		if (release is null) return ServiceResult.Fail(404, "Release not found");

		if (!release.CheckPerm(actor, Permission.DELETE_RELEASE))
			return ServiceResult.Fail(403, "Unable to delete the release");

		_db.Releases.Remove(release);
		await _db.SaveChangesAsync(ct);

		var key = release.GetObjectKey();
		if (key is not null)
			try { await _storage.DeleteAsync(key, ct); } catch (Exception ex) { _logger.LogWarning(ex, "删除对象失败 {Key}", key); }

		return ServiceResult.Ok(new { success = true });
	}

	public async Task<ServiceResult> ApproveReleaseAsync(User actor, Package package, int releaseId, CancellationToken ct = default)
	{
		var release = await _db.Releases.Include(r => r.Package).ThenInclude(p => p.Maintainers)
			.FirstOrDefaultAsync(r => r.Id == releaseId && r.PackageId == package.Id, ct);
		if (release is null) return ServiceResult.Fail(404, "Release not found");

		if (!release.CheckPerm(actor, Permission.APPROVE_RELEASE))
			return ServiceResult.Fail(403, "You cannot approve this release");

		if (release.State is ReleaseState.PROCESSING or ReleaseState.FAILED)
			return ServiceResult.Fail(400, "Release is not ready for approval");
		if (string.IsNullOrEmpty(release.Url))
			return ServiceResult.Fail(400, "Release has no file");

		release.State = ReleaseState.APPROVED;
		await _db.SaveChangesAsync(ct);
		return ServiceResult.Ok(new { success = true });
	}

	private static bool LooksLikeZip(MemoryStream s)
	{
		if (s.Length < 4) return false;
		var buf = s.GetBuffer();
		// PK\x03\x04 或空档案 PK\x05\x06
		return buf[0] == 0x50 && buf[1] == 0x4B && (buf[2] == 0x03 || buf[2] == 0x05 || buf[2] == 0x07);
	}

	private static string RandomString(int len)
	{
		var chars = new char[len];
		var rnd = Random.Shared;
		for (int i = 0; i < len; i++) chars[i] = RandomChars[rnd.Next(RandomChars.Length)];
		return new string(chars);
	}
}
