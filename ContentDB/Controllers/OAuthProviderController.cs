// ContentDB C# —— OAuth2 提供方端点(authorize / token / userinfo)
// 基于 OpenIddict 直通(passthrough)在 MVC 中实现授权决策。

using System.Security.Claims;
using ContentDB.Api.Auth;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace ContentDB.Api.Controllers;

public sealed class OAuthProviderController : Controller
{
	private readonly ICurrentUserAccessor _currentUser;

	public OAuthProviderController(ICurrentUserAccessor currentUser)
	{
		_currentUser = currentUser;
	}

	/// <summary>授权端点。用户须先通过本站(外部 OIDC)登录,然后签发授权码。</summary>
	[HttpGet("/oauth/authorize")]
	[HttpPost("/oauth/authorize")]
	public async Task<IActionResult> Authorize()
	{
		var request = HttpContext.GetOpenIddictServerRequest()
			?? throw new InvalidOperationException("OpenIddict request cannot be retrieved.");

		// 未登录 -> 触发本站登录(外部 OIDC)
		var result = await HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
		if (!result.Succeeded)
		{
			return Challenge(
				authenticationSchemes: CookieAuthenticationDefaults.AuthenticationScheme,
				properties: new AuthenticationProperties
				{
					RedirectUri = Request.PathBase + Request.Path + QueryString.Create(Request.HasFormContentType
						? Request.Form.ToList()
						: Request.Query.ToList())
				});
		}

		var user = await _currentUser.GetAsync(HttpContext.RequestAborted);
		if (user is null)
			return Forbid(CookieAuthenticationDefaults.AuthenticationScheme);

		var identity = new ClaimsIdentity(
			authenticationType: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
			nameType: Claims.Name,
			roleType: Claims.Role);

		identity.SetClaim(Claims.Subject, user.Id.ToString())
			.SetClaim(Claims.Name, user.Username)
			.SetClaim(Claims.PreferredUsername, user.Username)
			.SetClaim(Claims.Email, user.Email);

		identity.SetScopes(request.GetScopes());
		identity.SetDestinations(GetDestinations);

		return SignIn(new ClaimsPrincipal(identity), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
	}

	/// <summary>令牌端点。交换授权码/刷新令牌。</summary>
	[HttpPost("/oauth/token")]
	[Produces("application/json")]
	public async Task<IActionResult> Token()
	{
		var request = HttpContext.GetOpenIddictServerRequest()
			?? throw new InvalidOperationException("OpenIddict request cannot be retrieved.");

		if (!request.IsAuthorizationCodeGrantType() && !request.IsRefreshTokenGrantType())
			throw new InvalidOperationException("The specified grant type is not supported.");

		var result = await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
		var identity = new ClaimsIdentity(result.Principal!.Claims,
			authenticationType: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
			nameType: Claims.Name, roleType: Claims.Role);

		identity.SetDestinations(GetDestinations);
		return SignIn(new ClaimsPrincipal(identity), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
	}

	/// <summary>UserInfo 端点。</summary>
	[HttpGet("/oauth/userinfo")]
	[HttpPost("/oauth/userinfo")]
	public async Task<IActionResult> UserInfo()
	{
		var user = await _currentUser.GetAsync(HttpContext.RequestAborted);
		if (user is null)
			return Challenge(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

		return Json(new Dictionary<string, object?>
		{
			[Claims.Subject] = user.Id.ToString(),
			[Claims.Name] = user.Username,
			[Claims.PreferredUsername] = user.Username,
			[Claims.Email] = user.Email,
			[Claims.Picture] = user.ProfilePicUrl,
		});
	}

	private static IEnumerable<string> GetDestinations(Claim claim)
	{
		switch (claim.Type)
		{
			case Claims.Name:
			case Claims.PreferredUsername:
				yield return Destinations.AccessToken;
				if (claim.Subject!.HasScope(Scopes.Profile))
					yield return Destinations.IdentityToken;
				yield break;

			case Claims.Email:
				yield return Destinations.AccessToken;
				if (claim.Subject!.HasScope(Scopes.Email))
					yield return Destinations.IdentityToken;
				yield break;

			case "AspNet.Identity.SecurityStamp":
				yield break;

			default:
				yield return Destinations.AccessToken;
				yield break;
		}
	}
}
