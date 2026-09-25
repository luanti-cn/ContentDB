// ContentDB C# —— LuantiCN 客户端更新检查端点。
// 客户端(builtin/mainmenu/dlg_version_info.lua)每 2 天拉取一次 update_information_url,
// 解析契约:{"latest": {"version": "5.18.0", "version_code": 5018000, "url": "https://..."}}
// 未配置 UpdateInfo 节(或 Version 为空)时返回 404,客户端静默忽略。

using ContentDB.Core.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace ContentDB.Api.Controllers;

[ApiController]
public sealed class ReleaseInfoController : ControllerBase
{
	private readonly UpdateInfoOptions _options;

	public ReleaseInfoController(IOptions<UpdateInfoOptions> options)
	{
		_options = options.Value;
	}

	[HttpGet("/release_info.json")]
	public IActionResult Get()
	{
		var latest = _options.Latest;
		if (latest is null || string.IsNullOrEmpty(latest.Version))
			return NotFound();

		Response.Headers.CacheControl = "public, max-age=3600";
		// 用字典而非匿名对象:客户端读的是 snake_case 的 "version_code",
		// 而默认 JSON 序列化是 camelCase,会产出 "versionCode" 导致解析失败。
		return Ok(new Dictionary<string, object>
		{
			["latest"] = new Dictionary<string, object>
			{
				["version"] = latest.Version,
				["version_code"] = latest.VersionCode,
				["url"] = latest.Url ?? "",
			},
		});
	}
}
