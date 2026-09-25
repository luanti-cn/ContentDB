// ContentDB C# —— 登录 / 登出(交互式,走外部 OIDC)

using ContentDB.Api.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc;

namespace ContentDB.Api.Controllers;

public sealed class AccountController : Controller
{
	/// <summary>发起外部 OIDC 登录。</summary>
	[HttpGet("/login")]
	public IActionResult Login([FromQuery] string? returnUrl = null)
	{
		var redirect = SafeReturnUrl(returnUrl);
		return Challenge(new AuthenticationProperties { RedirectUri = redirect },
			OpenIdConnectDefaults.AuthenticationScheme);
	}

	/// <summary>登出:清本地 Cookie 会话(可选联动 OIDC 端登出)。</summary>
	[HttpGet("/logout")]
	[HttpPost("/logout")]
	public async Task<IActionResult> Logout()
	{
		await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
		return Redirect("/");
	}

	[HttpGet("/403")]
	public IActionResult Forbidden403() => StatusCode(403, new { error = "Access denied" });

	private string SafeReturnUrl(string? returnUrl)
	{
		if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
			return returnUrl;
		return "/";
	}
}
