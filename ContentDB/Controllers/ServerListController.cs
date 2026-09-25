// ContentDB C# —— Luanti 客户端服务器列表端点(/serverlists)。
// 客户端 serverlist_url 指向本前缀:{base}/list 与 {base}/geoip(serverlistmgr.lua)。

using ContentDB.Core.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace ContentDB.Api.Controllers;

[ApiController]
public sealed class ServerListController : ControllerBase
{
	private readonly IServerListService _serverList;

	public ServerListController(IServerListService serverList)
	{
		_serverList = serverList;
	}

	/// <summary>合并列表(本站收录 + 上游回源),官方 master 契约。</summary>
	[HttpGet("/serverlists/list")]
	public async Task<IActionResult> List(CancellationToken ct)
	{
		var min = GetIntQuery("proto_version_min", 39);
		var max = GetIntQuery("proto_version_max", 53);

		var body = await _serverList.GetMergedListJsonAsync(min, max, ct);
		Response.Headers.CacheControl = "public, max-age=60";
		return Content(body, "application/json");
	}

	/// <summary>geoip:返回请求者大洲(转发真实客户端 IP 给上游,失败兜底 AS)。</summary>
	[HttpGet("/serverlists/geoip")]
	public async Task<IActionResult> Geoip(CancellationToken ct)
	{
		var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
		var (body, status) = await _serverList.GetGeoipAsync(ip, ct);
		Response.Headers.CacheControl = "public, max-age=3600";
		Response.StatusCode = status;
		return Content(body, "application/json");
	}

	private int GetIntQuery(string key, int fallback)
		=> int.TryParse(Request.Query[key], out var v) && v > 0 ? v : fallback;
}
