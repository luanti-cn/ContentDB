// ContentDB C# —— 每日维护后台任务
// 替代原 Celery beat 的一部分:分数重算、删旧通知、NEW_MEMBER -> MEMBER 升级。
// 简化实现:每 6 小时跑一次;各步骤独立容错。

using ContentDB.Core.Domain;
using ContentDB.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContentDB.Api.BackgroundServices;

public sealed class MaintenanceWorker : BackgroundService
{
	private static readonly TimeSpan Interval = TimeSpan.FromHours(6);
	private static readonly TimeSpan NotificationRetention = TimeSpan.FromDays(90);

	private readonly IServiceProvider _services;
	private readonly ILogger<MaintenanceWorker> _logger;

	public MaintenanceWorker(IServiceProvider services, ILogger<MaintenanceWorker> logger)
	{
		_services = services;
		_logger = logger;
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		try { await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken); }
		catch (OperationCanceledException) { return; }

		while (!stoppingToken.IsCancellationRequested)
		{
			await RunOnceAsync(stoppingToken);
			try { await Task.Delay(Interval, stoppingToken); }
			catch (OperationCanceledException) { break; }
		}
	}

	private async Task RunOnceAsync(CancellationToken ct)
	{
		using var scope = _services.CreateScope();
		var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

		await Safe(() => RecalculateScoresAsync(db, ct), "分数重算");
		await Safe(() => DeleteOldNotificationsAsync(db, ct), "删除旧通知");
		await Safe(() => UpgradeNewMembersAsync(db, ct), "升级新会员");
	}

	private async Task Safe(Func<Task> action, string name)
	{
		try { await action(); _logger.LogInformation("维护任务完成:{Name}", name); }
		catch (Exception ex) { _logger.LogWarning(ex, "维护任务失败:{Name}", name); }
	}

	/// <summary>分数 = 下载分 + 评价分(每条已审核评价 ±150 权重)。</summary>
	private static async Task RecalculateScoresAsync(AppDbContext db, CancellationToken ct)
	{
		var packages = await db.Packages
			.Where(p => p.State == PackageState.APPROVED)
			.Select(p => new
			{
				Package = p,
				ReviewScore = db.Reviews
					.Where(r => r.PackageId == p.Id && r.Approved)
					.Sum(r => r.Rating > 3 ? 150.0 : r.Rating == 3 ? 0.0 : -150.0)
			})
			.ToListAsync(ct);

		foreach (var x in packages)
			x.Package.Score = x.Package.ScoreDownloads + x.ReviewScore;

		await db.SaveChangesAsync(ct);
	}

	private static async Task DeleteOldNotificationsAsync(AppDbContext db, CancellationToken ct)
	{
		var cutoff = DateTimeOffset.UtcNow - NotificationRetention;
		await db.Notifications.Where(n => n.Read && n.CreatedAt < cutoff).ExecuteDeleteAsync(ct);
	}

	/// <summary>注册满 7 天且有已审核包的 NEW_MEMBER 升级为 MEMBER。</summary>
	private static async Task UpgradeNewMembersAsync(AppDbContext db, CancellationToken ct)
	{
		var cutoff = DateTimeOffset.UtcNow.AddDays(-7);
		var candidates = await db.Users
			.Where(u => u.Rank == UserRank.NEW_MEMBER && u.CreatedAt < cutoff)
			.Where(u => db.Packages.Any(p => p.AuthorId == u.Id && p.State == PackageState.APPROVED))
			.ToListAsync(ct);

		foreach (var u in candidates)
			u.Rank = UserRank.MEMBER;

		if (candidates.Count > 0)
			await db.SaveChangesAsync(ct);
	}
}
