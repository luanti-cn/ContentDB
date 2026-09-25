// ContentDB C# —— 多源解析器实现

using ContentDB.Core.Abstractions;
using ContentDB.Core.Configuration;
using Microsoft.Extensions.Options;

namespace ContentDB.Infrastructure.Services;

public sealed class SourceResolver : ISourceResolver
{
	private readonly SourceSitesOptions _options;

	public SourceResolver(IOptions<SourceSitesOptions> options)
	{
		_options = options.Value;
	}

	public LocalSiteOptions Local => _options.Local;

	public IReadOnlyList<UpstreamSiteOptions> Upstreams
		=> _options.EnabledUpstreams().ToList();

	public UpstreamSiteOptions? FindById(string id) => _options.FindUpstream(id);

	public UpstreamSiteOptions? Default => _options.EnabledUpstreams().FirstOrDefault();
}
