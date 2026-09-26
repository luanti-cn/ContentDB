// ContentDB.Relay —— 入口
// 控制面:HTTP(仅内网,主后端持密钥调用 /allocate);
// 数据面:每房间一个 UDP 端口,Host 注册 + 双向转发;空闲房间定期回收。
//
// min配置(appsettings.json):
//   { "Relay": { "InternalSecret": "长随机串", "PublicAddress": "relay.luanti.cn" } }
// 主后端 appsettings.json:
//   { "Relay": { "Enabled": true, "BaseUrl": "http://127.0.0.1:5180",
//                "Secret": "同一长随机串", "PublicHost": "relay.luanti.cn" } }

using System.Security.Cryptography;
using System.Text.Json;
using ContentDB.Relay;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<RelayOptions>(builder.Configuration.GetSection(RelayOptions.SectionName));

// 监听地址走 Relay:Listen(不用默认 5000)
var relayConfig = builder.Configuration.GetSection(RelayOptions.SectionName).Get<RelayOptions>() ?? new RelayOptions();
if (!string.IsNullOrEmpty(relayConfig.Listen))
	builder.WebHost.UseUrls(relayConfig.Listen);

builder.Services.AddSingleton<RelayServer>();
builder.Services.AddHostedService<ReaperService>();

var app = builder.Build();

// 健康检查
app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

// 房间列表(运维)
app.MapGet("/rooms", (RelayServer relay, HttpContext ctx) =>
{
	if (!CheckSecret(ctx)) return Results.Unauthorized();
	return Results.Ok(new { rooms = relay.ListRooms() });
});

// 负载统计(横向扩展:后端调度最小负载节点用)
app.MapGet("/stats", (RelayServer relay, HttpContext ctx) =>
{
	if (!CheckSecret(ctx)) return Results.Unauthorized();
	var rooms = relay.ListRooms();
	return Results.Ok(new
	{
		rooms = rooms.Count,
		hosted = rooms.Count(r => r.HostRegistered),
		guests = rooms.Sum(r => r.Guests),
	});
});

// 分配房间端口(幂等:同 roomId 复用)
app.MapPost("/allocate", async (HttpContext ctx, RelayServer relay) =>
{
	if (!CheckSecret(ctx)) return Results.Unauthorized();
	var body = await JsonSerializer.DeserializeAsync<Dictionary<string, JsonElement>>(
		ctx.Request.Body, cancellationToken: ctx.RequestAborted);
	var roomId = body is not null && body.TryGetValue("roomId", out var r) && r.ValueKind == JsonValueKind.String
		? r.GetString() : null;
	if (string.IsNullOrWhiteSpace(roomId))
		return Results.BadRequest(new { error = "roomId is required" });

	var result = relay.Allocate(roomId!);
	return result is null
		? Results.StatusCode(503)
		: Results.Ok(new { roomId = result.RoomId, port = result.Port, publicAddress = result.PublicAddress, ticket = result.Ticket });
});

// 释放房间端口
app.MapPost("/deallocate", async (HttpContext ctx, RelayServer relay) =>
{
	if (!CheckSecret(ctx)) return Results.Unauthorized();
	var body = await JsonSerializer.DeserializeAsync<Dictionary<string, JsonElement>>(
		ctx.Request.Body, cancellationToken: ctx.RequestAborted);
	var roomId = body is not null && body.TryGetValue("roomId", out var r) && r.ValueKind == JsonValueKind.String
		? r.GetString() : null;
	if (string.IsNullOrWhiteSpace(roomId))
		return Results.BadRequest(new { error = "roomId is required" });
	return relay.Deallocate(roomId!) ? Results.Ok(new { success = true }) : Results.NotFound();
});

app.Run();

static bool CheckSecret(HttpContext ctx)
{
	var secret = ctx.RequestServices.GetRequiredService<IOptions<RelayOptions>>().Value.InternalSecret;
	if (string.IsNullOrEmpty(secret)) return false;
	var provided = ctx.Request.Headers["X-Relay-Secret"].ToString();
	return !string.IsNullOrEmpty(provided)
		&& CryptographicOperations.FixedTimeEquals(
			System.Text.Encoding.UTF8.GetBytes(provided),
			System.Text.Encoding.UTF8.GetBytes(secret));
}

/// <summary>定期回收空闲房间。</summary>
internal sealed class ReaperService(RelayServer relay, IOptions<RelayOptions> options) : BackgroundService
{
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		var interval = TimeSpan.FromSeconds(Math.Clamp(options.Value.RoomIdleSeconds / 2, 5, 60));
		while (!stoppingToken.IsCancellationRequested)
		{
			try
			{
				await Task.Delay(interval, stoppingToken);
				relay.ReapIdle();
			}
			catch (OperationCanceledException)
			{
				return;
			}
			catch (Exception)
			{
				// 单轮失败不影响后续
			}
		}
	}
}
