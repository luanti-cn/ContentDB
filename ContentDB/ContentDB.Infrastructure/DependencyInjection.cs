// ContentDB C# Mirror
// Infrastructure 层的 DI 注册:DbContext、S3、上游 HttpClient(带 Polly)、缓存/文件服务。

using System.Net.Http.Headers;
using Amazon.S3;
using ContentDB.Core.Abstractions;
using ContentDB.Core.Configuration;
using ContentDB.Infrastructure.Data;
using ContentDB.Infrastructure.Realtime;
using ContentDB.Infrastructure.Services;
using ContentDB.Infrastructure.Storage;
using ContentDB.Infrastructure.Upstream;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.Extensions.Http;

namespace ContentDB.Infrastructure;

public static class DependencyInjection
{
	public static IServiceCollection AddContentDbMirror(this IServiceCollection services, IConfiguration config)
	{
		// 配置绑定
		services.Configure<MirrorOptions>(config.GetSection(MirrorOptions.SectionName));
		services.Configure<StorageOptions>(config.GetSection(StorageOptions.SectionName));
		services.Configure<SourceSitesOptions>(config.GetSection(SourceSitesOptions.SectionName));
		services.Configure<VaultOptions>(config.GetSection(VaultOptions.SectionName));
		services.Configure<ServerListOptions>(config.GetSection(ServerListOptions.SectionName));
		services.Configure<Core.Configuration.RelayOptions>(config.GetSection(Core.Configuration.RelayOptions.SectionName));

		// EF Core / PostgreSQL
		// 上游缓存库(MirrorDb)与本地领域库(AppDb)可为同一实例的不同库或同库不同表。
		var mirrorConn = config.GetConnectionString("MirrorDb")
			?? "Host=localhost;Database=contentdb_mirror;Username=contentdb;Password=password";
		var appConn = config.GetConnectionString("AppDb") ?? mirrorConn;

		// AppDb(本地领域库)与 MirrorDb(上游缓存库)默认是两个独立数据库
		// (见 appsettings 的 ConnectionStrings)。各自的迁移写入各自库里的
		// 默认 __EFMigrationsHistory,互不干扰。
		// 注意:若把二者配成同一物理库,则需要为其中之一指定独立的
		// MigrationsHistoryTable,否则会共享同一张历史表而互相冲突。
		services.AddDbContext<MirrorDbContext>(opt => opt.UseNpgsql(mirrorConn));

		services.AddDbContext<AppDbContext>(opt => opt.UseNpgsql(appConn));

		// S3 兼容存储
		var storage = config.GetSection(StorageOptions.SectionName).Get<StorageOptions>() ?? new StorageOptions();
		services.AddSingleton<IAmazonS3>(_ =>
		{
			var s3Config = new AmazonS3Config
			{
				ForcePathStyle = storage.ForcePathStyle,
			};

			if (!string.IsNullOrEmpty(storage.ServiceUrl))
			{
				s3Config.ServiceURL = storage.ServiceUrl;
				// 关键:自定义 endpoint(腾讯云 COS / 阿里云 OSS / MinIO / Cloudflare R2)时,
				// SigV4 签名作用域仍需包含正确 region,否则默认 us-east-1 会与
				// 实际 region(如 ap-guangzhou;R2 用 auto)不一致,导致 SignatureDoesNotMatch(403)。
				s3Config.AuthenticationRegion = storage.Region;
			}
			else
			{
				s3Config.RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(storage.Region);
			}

			// 跨境访问(如 R2)延迟高:调大超时、收敛重试,避免 TaskCanceledException 及重试放大延迟。
			if (storage.TimeoutSeconds > 0)
				s3Config.Timeout = TimeSpan.FromSeconds(storage.TimeoutSeconds);
			if (storage.MaxErrorRetry >= 0)
				s3Config.MaxErrorRetry = storage.MaxErrorRetry;

			return new AmazonS3Client(storage.AccessKey, storage.SecretKey, s3Config);
		});
		services.AddScoped<IObjectStorage, S3ObjectStorage>();

		// 上游回源 HttpClient(带 Polly 重试)。
		// 多源:不设 BaseAddress,每次调用用绝对 URL(baseUrl 由各 source 提供)。
		var mirror = config.GetSection(MirrorOptions.SectionName).Get<MirrorOptions>() ?? new MirrorOptions();
		services.AddHttpClient<IUpstreamClient, UpstreamClient>(client =>
		{
			client.Timeout = TimeSpan.FromSeconds(mirror.UpstreamTimeoutSeconds);
			client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
		})
		.AddPolicyHandler(GetRetryPolicy());

		// 多源解析器
		services.AddSingleton<ISourceResolver, SourceResolver>();

		// 核心服务
		services.AddScoped<IMetadataCacheService, MetadataCacheService>();
		services.AddScoped<IFileMirrorService, FileMirrorService>();
		services.AddScoped<IMergedReadService, MergedReadService>();
		services.AddScoped<IDownloadService, DownloadService>();
		services.AddScoped<IPackageWriteService, PackageWriteService>();
		services.AddScoped<IReleaseWriteService, ReleaseWriteService>();
		services.AddScoped<IScreenshotWriteService, ScreenshotWriteService>();
		services.AddScoped<IThreadWriteService, ThreadWriteService>();
		services.AddScoped<IReviewWriteService, ReviewWriteService>();
		services.AddScoped<ICollectionWriteService, CollectionWriteService>();
		services.AddScoped<ITokenService, TokenService>();
		services.AddScoped<IGitReleasePackager, GitReleasePackager>();
		services.AddScoped<IThumbnailService, ThumbnailService>();
		services.AddSingleton<IZipMetadataExtractor, ZipMetadataExtractor>();
		services.AddSingleton<IMarkdownService, MarkdownService>();
		services.AddScoped<INotificationService, NotificationService>();

		// 服务器列表(/serverlists):上游回源 + 本站收录合并。
		services.AddScoped<IServerListService, ServerListService>();

		// 云同步:保管库加密 / 设备配对 / 服务器账号保管库 / 角色
		services.AddSingleton<IVaultCrypto, VaultCrypto>();
		services.AddScoped<IDevicePairingService, DevicePairingService>();
		services.AddScoped<ICloudVaultService, CloudVaultService>();
		services.AddScoped<IPlayerCharacterService, PlayerCharacterService>();
		services.AddScoped<IFriendService, FriendService>();
		services.AddScoped<IGameServerService, GameServerService>();
		services.AddScoped<IPartyService, PartyService>();

		// 实时通讯:WS 推送中枢(单例)/ 好友私聊 / 联机房间(打洞信令)
		services.AddSingleton<Realtime.RealtimeHub>();
		services.AddSingleton<IRealtimeHub>(sp => sp.GetRequiredService<Realtime.RealtimeHub>());
		services.AddSingleton<MessageRateLimiter>();
		services.AddSingleton<HostRoomStore>();
		services.AddScoped<IDirectMessageService, DirectMessageService>();
		services.AddScoped<IHostRoomService, HostRoomService>();

		// 中继客户端(房间 UDP 中继分配;未配置或不可用时降级纯 P2P)
		var relayOptions = config.GetSection(Core.Configuration.RelayOptions.SectionName)
			.Get<Core.Configuration.RelayOptions>() ?? new Core.Configuration.RelayOptions();
		services.AddHttpClient<IRelayClient, RelayHttpClient>(client =>
		{
			if (!string.IsNullOrEmpty(relayOptions.BaseUrl))
				client.BaseAddress = new Uri(relayOptions.BaseUrl);
			client.Timeout = TimeSpan.FromSeconds(5);
		});

		return services;
	}

	private static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
	{
		return HttpPolicyExtensions
			.HandleTransientHttpError()
			.WaitAndRetryAsync(2, retryAttempt => TimeSpan.FromMilliseconds(300 * Math.Pow(2, retryAttempt)));
	}
}
