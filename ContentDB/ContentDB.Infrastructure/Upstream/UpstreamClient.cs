// ContentDB C# Mirror
// IUpstreamClient 实现,基于无 BaseAddress 的命名 HttpClient(每次用绝对 URL)。
// 支持多源:baseUrl 由调用方(按 source)指定。

using ContentDB.Core.Abstractions;
using ContentDB.Core.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ContentDB.Infrastructure.Upstream;

public sealed class UpstreamClient : IUpstreamClient
{
	public const string HttpClientName = "upstream";

	private readonly HttpClient _http;
	private readonly MirrorOptions _options;
	private readonly ILogger<UpstreamClient> _logger;

	public UpstreamClient(HttpClient http, IOptions<MirrorOptions> options, ILogger<UpstreamClient> logger)
	{
		_http = http;
		_options = options.Value;
		_logger = logger;
	}

	private static string Combine(string baseUrl, string pathAndQuery)
	{
		if (pathAndQuery.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
			pathAndQuery.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
			return pathAndQuery;
		return baseUrl.TrimEnd('/') + "/" + pathAndQuery.TrimStart('/');
	}

	public async Task<UpstreamJsonResponse> GetJsonAsync(
		string baseUrl,
		string relativePathAndQuery,
		string? acceptLanguage = null,
		string? userAgent = null,
		CancellationToken ct = default,
		IReadOnlyDictionary<string, string>? extraHeaders = null)
	{
		var url = Combine(baseUrl, relativePathAndQuery);
		using var request = new HttpRequestMessage(HttpMethod.Get, url);
		request.Headers.TryAddWithoutValidation("Accept", "application/json");
		if (!string.IsNullOrEmpty(acceptLanguage))
			request.Headers.TryAddWithoutValidation("Accept-Language", acceptLanguage);
		if (_options.ForwardUserAgent && !string.IsNullOrEmpty(userAgent))
			request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
		if (extraHeaders is not null)
		{
			foreach (var (name, value) in extraHeaders)
				request.Headers.TryAddWithoutValidation(name, value);
		}

		using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct);
		var body = await response.Content.ReadAsStringAsync(ct);
		var contentType = response.Content.Headers.ContentType?.ToString();

		_logger.LogDebug("回源 GET {Url} -> {Status}", url, (int)response.StatusCode);

		return new UpstreamJsonResponse((int)response.StatusCode, body, contentType);
	}

	public async Task<UpstreamFileResponse> GetFileAsync(
		string baseUrl,
		string absoluteOrRelativeUrl,
		string? userAgent = null,
		CancellationToken ct = default)
	{
		var url = Combine(baseUrl, absoluteOrRelativeUrl);
		using var request = new HttpRequestMessage(HttpMethod.Get, url);
		if (_options.ForwardUserAgent && !string.IsNullOrEmpty(userAgent))
			request.Headers.TryAddWithoutValidation("User-Agent", userAgent);

		// 用 ResponseHeadersRead 以便流式读取大文件。
		var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

		if (!response.IsSuccessStatusCode)
		{
			var status = (int)response.StatusCode;
			response.Dispose();
			return new UpstreamFileResponse(status, null, null, null);
		}

		var stream = await response.Content.ReadAsStreamAsync(ct);
		var contentType = response.Content.Headers.ContentType?.ToString();
		var length = response.Content.Headers.ContentLength;
		var finalUrl = response.RequestMessage?.RequestUri?.ToString();

		return new UpstreamFileResponse((int)response.StatusCode, stream, contentType, length, finalUrl);
	}
}
