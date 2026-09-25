// ContentDB C# Mirror —— 程序入口
// 国内镜像/代理服务:回源官方 ContentDB + 懒缓存元数据 + S3 存储文件。

using ContentDB.Api.Auth;
using ContentDB.Api.BackgroundServices;
using ContentDB.Core.Configuration;
using ContentDB.Infrastructure;
using ContentDB.Infrastructure.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddHttpContextAccessor();

// LuantiCN 客户端更新检查(/release_info.json)。
builder.Services.Configure<UpdateInfoOptions>(
	builder.Configuration.GetSection(UpdateInfoOptions.SectionName));

// 反向代理场景:信任转发头以获取真实客户端 IP / 协议(国内必在 Nginx/CDN 后)。
builder.Services.Configure<ForwardedHeadersOptions>(opt =>
{
	opt.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
		| Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto;
	// 生产环境应把上游代理 IP 加入 KnownProxies/KnownIPNetworks;此处清空表示信任任意(仅便于起步)。
	opt.KnownIPNetworks.Clear();
	opt.KnownProxies.Clear();
});

// 镜像基础设施(DbContext / S3 / 上游 HttpClient / 缓存与文件服务)。
builder.Services.AddContentDbMirror(builder.Configuration);

// OAuth2 提供方(OpenIddict)—— 必须在 AddContentDbAuth 之前注册好 core/server/validation。
builder.Services.AddContentDbOAuthProvider();

// 认证/授权(外部 OIDC 登录 + API Token Bearer + Cookie 会话)。
builder.Services.AddContentDbAuth(builder.Configuration);

// 后台任务:定期刷新热门元数据缓存。
builder.Services.AddHostedService<CacheRefreshService>();
// 后台任务:git 发布打包 worker(PROCESSING -> APPROVED/FAILED)。
builder.Services.AddHostedService<GitReleaseWorker>();
// 后台任务:每日维护(分数重算/更新检测/删旧通知/升级会员)。
builder.Services.AddHostedService<MaintenanceWorker>();

var app = builder.Build();

app.UseForwardedHeaders();

// 应用数据库迁移(本地领域库 + 上游缓存库)。
using (var scope = app.Services.CreateScope())
{
	await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
	await scope.ServiceProvider.GetRequiredService<MirrorDbContext>().Database.MigrateAsync();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// 健康检查
app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

app.Run();
