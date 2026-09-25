// ContentDB C# —— 写 API 公共基类
// 提供当前用户解析、权限断言、统一 JSON 错误响应。

using ContentDB.Api.Auth;
using ContentDB.Core.Abstractions;
using ContentDB.Core.Domain;
using Microsoft.AspNetCore.Mvc;

namespace ContentDB.Api.Controllers;

public abstract class ApiControllerBase : ControllerBase
{
	protected ICurrentUserAccessor CurrentUser { get; }

	protected ApiControllerBase(ICurrentUserAccessor currentUser)
	{
		CurrentUser = currentUser;
	}

	protected string? UserAgent => Request.Headers.UserAgent.ToString() is { Length: > 0 } ua ? ua : null;

	protected IActionResult ApiError(int status, string message)
		=> StatusCode(status, new { success = false, error = message });

	protected async Task<User?> GetUserAsync() => await CurrentUser.GetAsync(HttpContext.RequestAborted);

	/// <summary>要求已认证并返回用户;否则抛出 401(由调用方转成响应)。</summary>
	protected async Task<(User? user, IActionResult? error)> RequireUserAsync()
	{
		var user = await GetUserAsync();
		if (user is null)
			return (null, ApiError(401, "Authentication needed"));
		if (user.IsBanned)
			return (null, ApiError(403, "Account is banned"));
		return (user, null);
	}

	/// <summary>把领域 ServiceResult 转成 HTTP 响应。</summary>
	protected IActionResult FromResult(ServiceResult result)
	{
		if (result.Success)
			return StatusCode(result.StatusCode, result.Payload ?? new { success = true });
		return StatusCode(result.StatusCode, new { success = false, error = result.Error });
	}
}
