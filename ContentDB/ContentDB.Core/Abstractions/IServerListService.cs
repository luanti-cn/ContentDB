// ContentDB C# —— Luanti 客户端服务器列表(/serverlists)服务抽象。
// 列表 = 上游官方 master(回源缓存) + 本站收录(Listed)的服务器,合并输出。

namespace ContentDB.Core.Abstractions;

public interface IServerListService
{
	/// <summary>返回与官方 master 契约一致的 {"list":[...]} JSON(本站 + 上游合并)。</summary>
	Task<string> GetMergedListJsonAsync(int protoMin, int protoMax, CancellationToken ct = default);

	/// <summary>geoip:按客户端 IP 请求上游大洲信息,失败时返回兜底大洲。</summary>
	Task<(string Body, int StatusCode)> GetGeoipAsync(string? clientIp, CancellationToken ct = default);
}
