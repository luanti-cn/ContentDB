// ContentDB C# —— 中继客户端实现(主后端 → Relay 控制面)
// 多节点:单 HttpClient,按节点用绝对 URL + 节点密钥。
// 短超时、失败静默返回 null:中继不可用时房间仍可纯 P2P 工作。

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ContentDB.Core.Abstractions;
using ContentDB.Core.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ContentDB.Infrastructure.Services;

public sealed class RelayHttpClient : IRelayClient
{
	private readonly HttpClient _http;
	private readonly RelayOptions _options;
	private readonly ILogger<RelayHttpClient> _logger;
	private readonly IReadOnlyList<RelayNodeInfo> _nodes;

	public RelayHttpClient(HttpClient http, IOptions<RelayOptions> options, ILogger<RelayHttpClient> logger)
	{
		_http = http;
		_options = options.Value;
		_logger = logger;

		if (_options.Enabled)
		{
			if (_options.Nodes.Count > 0)
			{
				_nodes = _options.Nodes
					.Where(n => !string.IsNullOrEmpty(n.Name) && !string.IsNullOrEmpty(n.BaseUrl)
						&& !string.IsNullOrEmpty(n.PublicHost))
					.Select(n => new RelayNodeInfo(n.Name, n.PublicHost))
					.ToList();
			}
			else if (!string.IsNullOrEmpty(_options.BaseUrl) && !string.IsNullOrEmpty(_options.PublicHost))
			{
				_nodes = [new RelayNodeInfo("default", _options.PublicHost)]; // 单节点向后兼容
			}
			else
			{
				_nodes = [];
			}
		}
		else
		{
			_nodes = [];
		}
	}

	public IReadOnlyList<RelayNodeInfo> Nodes => _nodes;

	public async Task<RelayAllocation?> AllocateAsync(string node, string roomId, CancellationToken ct = default)
	{
		var json = await SendAsync(node, HttpMethod.Post, "/allocate", new { roomId }, ct);
		if (json is null) return null;

		try
		{
			var port = json.Value.GetProperty("port").GetInt32();
			var ticket = json.Value.GetProperty("ticket").GetString() ?? "";
			return new RelayAllocation(roomId, port, ticket);
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "中继分配响应格式异常(node={Node}, roomId={RoomId})", node, roomId);
			return null;
		}
	}

	public async Task DeallocateAsync(string node, string roomId, CancellationToken ct = default)
	{
		await SendAsync(node, HttpMethod.Post, "/deallocate", new { roomId }, ct);
	}

	/// <summary>带 3s 超时调用节点;失败返回 null(不抛出,降级纯 P2P)。</summary>
	private async Task<JsonElement?> SendAsync(string node, HttpMethod method, string path, object body, CancellationToken ct)
	{
		var nodeOptions = ResolveNode(node);
		if (nodeOptions is null) return null;

		try
		{
			using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
			using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);

			using var request = new HttpRequestMessage(method, new Uri(new Uri(nodeOptions.BaseUrl), path))
			{
				Content = JsonContent.Create(body),
			};
			var secret = string.IsNullOrEmpty(nodeOptions.Secret) ? _options.Secret : nodeOptions.Secret;
			if (!string.IsNullOrEmpty(secret))
				request.Headers.Add("X-Relay-Secret", secret);
			request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

			using var response = await _http.SendAsync(request, linked.Token);
			if (!response.IsSuccessStatusCode)
			{
				_logger.LogWarning("中继调用失败 {Status}(node={Node}, {Path})", (int)response.StatusCode, node, path);
				return null;
			}
			if (response.Content.Headers.ContentType?.MediaType?.Contains("json") != true)
				return null;

			return await response.Content.ReadFromJsonAsync<JsonElement>(linked.Token);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.LogWarning("中继不可用(node={Node},{Path}),房间降级纯 P2P:{Message}", node, path, ex.Message);
			return null;
		}
		catch (OperationCanceledException) when (!ct.IsCancellationRequested)
		{
			// 3s 节点超时(外层 ct 未取消):视为节点不可用
			_logger.LogWarning("中继超时(node={Node},{Path}),房间降级纯 P2P", node, path);
			return null;
		}
	}

	private RelayNodeOptions? ResolveNode(string node)
	{
		if (!_options.Enabled) return null;

		if (node == "default")
		{
			return string.IsNullOrEmpty(_options.BaseUrl) ? null : new RelayNodeOptions
			{
				Name = "default",
				BaseUrl = _options.BaseUrl,
				Secret = _options.Secret,
				PublicHost = _options.PublicHost,
			};
		}

		return _options.Nodes.FirstOrDefault(n => n.Name == node && !string.IsNullOrEmpty(n.BaseUrl));
	}
}
