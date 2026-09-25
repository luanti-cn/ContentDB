// ContentDB C# Mirror
// 后台服务:定期预热/刷新热门只读端点的缓存,降低客户端首次访问延迟。
// 替代原 Python 项目里的 Celery beat 定时任务(仅镜像相关部分)。

using ContentDB.Core.Abstractions;
using ContentDB.Core.Configuration;
using Microsoft.Extensions.Options;

namespace ContentDB.Api.BackgroundServices;

public sealed class CacheRefreshService : BackgroundService
{
	private readonly IServiceProvider _services;
	private readonly ILogger<CacheRefreshService> _logger;
	private readonly MirrorOptions _options;

	// 需要周期性预热的核心端点(客户端启动即用)。
	private static readonly string[] PrewarmPaths =
	[
		"/api/packages/?type=mod&hide=nonfree",
		"/api/packages/",
		"/api/updates/",
		"/api/homepage/",
		"/api/tags/",
		"/api/licenses/",
		"/api/content_warnings/",
		"/api/minetest_versions/",
	];

	public CacheRefreshService(
		IServiceProvider services,
		IOptions<MirrorOptions> options,
		ILogger<CacheRefreshService> logger)
	{
		_services = services;
		_options = options.Value;
		_logger = logger;
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		// 启动稍作延迟,待应用就绪。
		try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); }
		catch (OperationCanceledException) { return; }

		while (!stoppingToken.IsCancellationRequested)
		{
			await PrewarmAsync(stoppingToken);

			try
			{
				// 刷新间隔取 packages TTL 的一半,保证缓存基本常新。
				var interval = Math.Max(60, _options.MetadataTtlSeconds.Packages / 2);
				await Task.Delay(TimeSpan.FromSeconds(interval), stoppingToken);
			}
			catch (OperationCanceledException)
			{
				break;
			}
		}
	}

	private async Task PrewarmAsync(CancellationToken ct)
	{
		using var scope = _services.CreateScope();
		var cache = scope.ServiceProvider.GetRequiredService<IMetadataCacheService>();
		var sources = scope.ServiceProvider.GetRequiredService<ISourceResolver>();

		// 对每个启用的上游站点分别预热。
		foreach (var site in sources.Upstreams)
		{
			foreach (var path in PrewarmPaths)
			{
				if (ct.IsCancellationRequested) break;
				try
				{
					await cache.GetOrFetchAsync(site, path, _options.MetadataTtlSeconds.Default, ct: ct);
					_logger.LogDebug("已预热 [{Source}] {Path}", site.Id, path);
				}
				catch (Exception ex)
				{
					_logger.LogWarning(ex, "预热失败 [{Source}] {Path}", site.Id, path);
				}
			}
		}
	}
}
