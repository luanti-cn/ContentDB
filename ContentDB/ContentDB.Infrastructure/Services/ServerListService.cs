// ContentDB C# —— Luanti 客户端服务器列表(/serverlists)实现。
// 列表 = 上游官方 master(回源 + TTL 缓存 + single-flight) + 本站收录(Listed)合并输出;
// 上游不可用时降级:陈旧缓存 → 仅本站收录。geoip 代理转发客户端 IP(X-Forwarded-For)。

using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using ContentDB.Core.Abstractions;
using ContentDB.Core.Configuration;
using ContentDB.Core.Domain;
using ContentDB.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ContentDB.Infrastructure.Services;

public sealed class ServerListService : IServerListService
{
	private readonly AppDbContext _db;
	private readonly IUpstreamClient _upstream;
	private readonly ServerListOptions _options;
	private readonly ILogger<ServerListService> _logger;

	// 列表缓存(按协议范围区分):同一 key 的并发请求 single-flight 合并为一次回源。
	private static readonly ConcurrentDictionary<string, (string Body, DateTimeOffset FetchedAt)> Cache = new();
	private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new();

	// geoip 兜底结果的短缓存:上游持续不可达时避免每个请求都吃超时等待。
	private static readonly (string Body, DateTimeOffset FetchedAt)[] GeoipFallback = new (string, DateTimeOffset)[1];

	public ServerListService(
		AppDbContext db,
		IUpstreamClient upstream,
		IOptions<ServerListOptions> options,
		ILogger<ServerListService> logger)
	{
		_db = db;
		_upstream = upstream;
		_options = options.Value;
		_logger = logger;
	}

	public async Task<string> GetMergedListJsonAsync(int protoMin, int protoMax, CancellationToken ct = default)
	{
		var cacheKey = $"list:{protoMin}:{protoMax}";
		var gate = Gates.GetOrAdd(cacheKey, _ => new SemaphoreSlim(1, 1));
		await gate.WaitAsync(ct);
		try
		{
			return await GetMergedListCoreAsync(protoMin, protoMax, cacheKey, ct);
		}
		finally
		{
			gate.Release();
		}
	}

	private async Task<string> GetMergedListCoreAsync(int protoMin, int protoMax, string cacheKey, CancellationToken ct)
	{
		var now = DateTimeOffset.UtcNow;
		var upstreamBody = await FetchUpstreamListAsync(protoMin, protoMax, cacheKey, now, ct);

		// 本站收录的服务器(上架的),认证优先、有上报的次之。
		var localServers = await _db.GameServers.AsNoTracking()
			.Where(s => s.Listed)
			.OrderByDescending(s => s.Verified)
			.ThenByDescending(s => s.ReportedAt)
			.ToListAsync(ct);

		var localNodes = localServers.Select(ShapeServer).ToList();

		// 合并:上游 {"list":[...]} 追加本站条目。
		if (upstreamBody is not null)
		{
			try
			{
				if (JsonNode.Parse(upstreamBody) is JsonObject root)
				{
					if (root["list"] is not JsonArray arr)
						root["list"] = arr = new JsonArray();
					foreach (var node in localNodes)
						arr.Add(node);
					return root.ToJsonString();
				}
				_logger.LogWarning("上游列表 JSON 结构不符合预期(缺少 list 字段),退回本地列表");
			}
			catch (JsonException ex)
			{
				_logger.LogWarning(ex, "上游列表 JSON 解析失败,退回本地列表");
			}
		}

		// 上游不可用:陈旧缓存也能用(总比没有强),追加本站条目。
		if (Cache.TryGetValue(cacheKey, out var stale))
		{
			try
			{
				if (JsonNode.Parse(stale.Body) is JsonObject root)
				{
					if (root["list"] is not JsonArray arr)
						root["list"] = arr = new JsonArray();
					foreach (var node in localNodes)
						arr.Add(node);
					return root.ToJsonString();
				}
			}
			catch (JsonException)
			{
				// fall through
			}
		}

		// 最后兜底:仅本站收录。
		return new JsonObject { ["list"] = new JsonArray(localNodes.ToArray()) }.ToJsonString();
	}

	/// <summary>回源上游列表(带 TTL 缓存);失败返回 null,并保留旧缓存供降级。</summary>
	private async Task<string?> FetchUpstreamListAsync(int protoMin, int protoMax, string cacheKey, DateTimeOffset now, CancellationToken ct)
	{
		if (Cache.TryGetValue(cacheKey, out var hit)
				&& now - hit.FetchedAt < TimeSpan.FromSeconds(_options.TtlSeconds))
			return hit.Body;

		try
		{
			var path = $"/list?proto_version_min={protoMin}&proto_version_max={protoMax}";
			var resp = await _upstream.GetJsonAsync(_options.Upstream, path, ct: ct);
			if (resp.StatusCode == 200 && !string.IsNullOrEmpty(resp.Body))
			{
				Cache[cacheKey] = (resp.Body, now);
				return resp.Body;
			}
			_logger.LogWarning("服务器列表回源失败 {Upstream} -> {Status}", _options.Upstream, resp.StatusCode);
		}
		catch (Exception ex) when (!ct.IsCancellationRequested)
		{
			_logger.LogWarning(ex, "服务器列表回源异常 {Upstream}", _options.Upstream);
		}
		return null;
	}

	/// <summary>把本站 GameServer 投影为官方 master 列表条目结构。</summary>
	private JsonObject ShapeServer(GameServer s)
	{
		// Address 规范化为 host[:port](端口缺省 30000)。
		var host = s.Address;
		var port = 30000;
		var idx = s.Address.LastIndexOf(':');
		if (idx > 0 && int.TryParse(s.Address[(idx + 1)..], out var parsed))
		{
			host = s.Address[..idx];
			port = parsed;
		}

		return new JsonObject
		{
			["name"] = s.Name,
			["address"] = host,
			["port"] = port,
			["description"] = s.Description ?? s.Motd ?? "",
			["clients"] = s.PlayersOnline,
			["clients_max"] = s.PlayersMax,
			["creative"] = false,
			["damage"] = true,
			["pvp"] = false,
			["proto_min"] = _options.LocalProtoMin,
			["proto_max"] = _options.LocalProtoMax,
			["gameid"] = _options.LocalGameId,
			["version"] = _options.LocalVersion,
			["uptime"] = 0,
			["ping"] = 0,
			["url"] = s.WebsiteUrl,
			["geo_continent"] = _options.GeoipFallbackContinent,
			["clients_list"] = new JsonArray(),
			["mods"] = new JsonArray(),
		};
	}

	public async Task<(string Body, int StatusCode)> GetGeoipAsync(string? clientIp, CancellationToken ct = default)
	{
		var now = DateTimeOffset.UtcNow;
		var (fbBody, fbAt) = GeoipFallback[0];
		if (fbBody is not null && now - fbAt < TimeSpan.FromSeconds(60))
			return (fbBody, 200);

		var headers = new Dictionary<string, string>();
		if (!string.IsNullOrEmpty(clientIp))
			headers["X-Forwarded-For"] = clientIp;

		try
		{
			var resp = await _upstream.GetJsonAsync(_options.Upstream, "/geoip", ct: ct, extraHeaders: headers);
			if (resp.StatusCode == 200 && !string.IsNullOrEmpty(resp.Body))
				return (resp.Body, 200);
		}
		catch (Exception ex) when (!ct.IsCancellationRequested)
		{
			_logger.LogWarning(ex, "geoip 回源失败,使用兜底大洲");
		}

		var body = JsonSerializer.Serialize(new { continent = _options.GeoipFallbackContinent });
		GeoipFallback[0] = (body, now);
		return (body, 200);
	}
}
