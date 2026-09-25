// ContentDB C# —— git 发布打包后台 worker
// 轮询 PROCESSING 且带 TaskId 的发布,克隆打包上传,成功置 APPROVED,失败置 FAILED。

using ContentDB.Core.Abstractions;
using ContentDB.Core.Domain;
using ContentDB.Infrastructure.Data;
using ContentDB.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace ContentDB.Api.BackgroundServices;

public sealed class GitReleaseWorker : BackgroundService
{
	private readonly IServiceProvider _services;
	private readonly ILogger<GitReleaseWorker> _logger;

	public GitReleaseWorker(IServiceProvider services, ILogger<GitReleaseWorker> logger)
	{
		_services = services;
		_logger = logger;
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		try { await Task.Delay(TimeSpan.FromSeconds(8), stoppingToken); }
		catch (OperationCanceledException) { return; }

		while (!stoppingToken.IsCancellationRequested)
		{
			try
			{
				await ProcessPendingAsync(stoppingToken);
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "git 打包 worker 循环异常");
			}

			try { await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken); }
			catch (OperationCanceledException) { break; }
		}
	}

	private async Task ProcessPendingAsync(CancellationToken ct)
	{
		using var scope = _services.CreateScope();
		var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
		var packager = scope.ServiceProvider.GetRequiredService<IGitReleasePackager>();

		// 取一批待处理的 git 发布(PROCESSING + 有 TaskId + 包有 repo)
		var pending = await db.Releases
			.Include(r => r.Package)
			.Where(r => r.State == ReleaseState.PROCESSING && r.TaskId != null)
			.OrderBy(r => r.Id)
			.Take(5)
			.ToListAsync(ct);

		foreach (var release in pending)
		{
			if (ct.IsCancellationRequested) break;

			var repo = release.Package.Repo;
			var gitRef = release.CommitHash; // CreateVcsReleaseAsync 里把 ref 暂存于 CommitHash
			if (string.IsNullOrEmpty(repo) || string.IsNullOrEmpty(gitRef))
			{
				release.State = ReleaseState.FAILED;
				release.TaskId = null;
				await db.SaveChangesAsync(ct);
				continue;
			}

			_logger.LogInformation("开始打包 git 发布 #{Id} {Repo}@{Ref}", release.Id, repo, gitRef);
			var result = await packager.PackAsync(repo, gitRef, release.Package.Name, ct);

			if (result.Success && result.ObjectKey is not null)
			{
				release.Url = "/" + result.ObjectKey;
				release.FileSizeBytes = result.SizeBytes;
				release.CommitHash = result.CommitHash;
				release.TaskId = null;
				release.State = ReleaseState.APPROVED;
				_logger.LogInformation("git 发布 #{Id} 打包成功 -> {Url}", release.Id, release.Url);
			}
			else
			{
				release.State = ReleaseState.FAILED;
				release.TaskId = null;
				_logger.LogWarning("git 发布 #{Id} 打包失败: {Error}", release.Id, result.Error);
			}

			await db.SaveChangesAsync(ct);
		}
	}
}
