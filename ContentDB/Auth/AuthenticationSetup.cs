// ContentDB C# —— 认证/授权装配
// 1) 外部 OIDC 登录(Cookie + OpenIdConnect,兼容 Casdoor/Logto/Keycloak)
// 2) API Token(Bearer)—— 见 ApiTokenAuthenticationHandler
// 3) 本站作 OAuth2 提供方(OpenIddict server)

using System.Security.Claims;
using ContentDB.Api.Auth;
using ContentDB.Core.Configuration;
using ContentDB.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using OpenIddict.Abstractions;

namespace ContentDB.Api.Auth;

public static class AuthenticationSetup
{
	public const string ApiTokenScheme = "ApiToken";
	public const string DeviceTokenScheme = "DeviceToken";

	public static IServiceCollection AddContentDbAuth(this IServiceCollection services, IConfiguration config)
	{
		var auth = config.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();
		services.Configure<AuthOptions>(config.GetSection(AuthOptions.SectionName));

		var authBuilder = services.AddAuthentication(options =>
		{
			options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
			options.DefaultChallengeScheme = auth.Oidc.Enabled
				? OpenIdConnectDefaults.AuthenticationScheme
				: CookieAuthenticationDefaults.AuthenticationScheme;
		});

		authBuilder.AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, o =>
		{
			o.LoginPath = "/login";
			o.LogoutPath = "/logout";
			o.AccessDeniedPath = "/403";
			o.ExpireTimeSpan = TimeSpan.FromDays(30);
			o.SlidingExpiration = true;
			o.Cookie.SameSite = SameSiteMode.Lax;
			o.Cookie.HttpOnly = true;
			// HTTP 开发下不强制 Secure,否则 http://localhost 会话 cookie 会被丢弃。
			if (!auth.Oidc.SecureCookies)
				o.Cookie.SecurePolicy = Microsoft.AspNetCore.Http.CookieSecurePolicy.SameAsRequest;
		});

		// API Token(Bearer)自定义方案
		authBuilder.AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, ApiTokenAuthenticationHandler>(
			ApiTokenScheme, _ => { });

		// 设备 Token(Bearer)方案 —— 云同步配对设备专用
		authBuilder.AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, DeviceTokenAuthenticationHandler>(
			DeviceTokenScheme, _ => { });

		// 外部 OIDC 登录
		if (auth.Oidc.Enabled)
		{
			authBuilder.AddOpenIdConnect(OpenIdConnectDefaults.AuthenticationScheme, o =>
			{
				o.Authority = auth.Oidc.Authority;
				o.ClientId = auth.Oidc.ClientId;
				o.ClientSecret = auth.Oidc.ClientSecret;
				o.ResponseType = OpenIdConnectResponseType.Code;
				// 回调用 GET(query)而非默认 form_post:form_post 是跨站 POST,SameSite=Lax 的
				// correlation/nonce cookie 不会被浏览器发送,导致 "Correlation failed"。
				// query 模式是顶层 GET 导航,Lax cookie 正常发送,HTTP 本地开发即可工作。
				o.ResponseMode = OpenIdConnectResponseMode.Query;
				o.UsePkce = true;
				o.RequireHttpsMetadata = auth.Oidc.RequireHttpsMetadata;
				o.CallbackPath = auth.Oidc.CallbackPath;
				o.SaveTokens = true;
				o.GetClaimsFromUserInfoEndpoint = true;
				o.MapInboundClaims = false;

				// PAR(Pushed Authorization Requests):部分 IdP/配置下会走失败路径,默认关闭回退到标准前端跳转。
				o.PushedAuthorizationBehavior = auth.Oidc.UsePushedAuthorization
					? Microsoft.AspNetCore.Authentication.OpenIdConnect.PushedAuthorizationBehavior.UseIfAvailable
					: Microsoft.AspNetCore.Authentication.OpenIdConnect.PushedAuthorizationBehavior.Disable;

				// 相关性/nonce cookie:HTTP 开发(非 https)下用 SameSite=Lax + 非强制 Secure,
				// 否则默认 SameSite=None+Secure 在 http://localhost 会被浏览器丢弃,导致回调 correlation 失败。
				// SecureCookies 与 RequireHttpsMetadata(IdP 元数据 HTTPS)相互独立。
				o.CorrelationCookie.SameSite = SameSiteMode.Lax;
				o.NonceCookie.SameSite = SameSiteMode.Lax;
				// 默认 correlation cookie 的 Path 是 CallbackPath(/signin-oidc)。经 Next dev 代理(:3000 -> :5175)
				// 转发时,浏览器对该窄 Path 的 cookie 回传在部分情况下会丢失,导致 "Correlation failed"。
				// 放宽 Path=/ 保证回调时一定带上。
				o.CorrelationCookie.Path = "/";
				o.NonceCookie.Path = "/";
				if (!auth.Oidc.SecureCookies)
				{
					o.CorrelationCookie.SecurePolicy = Microsoft.AspNetCore.Http.CookieSecurePolicy.SameAsRequest;
					o.NonceCookie.SecurePolicy = Microsoft.AspNetCore.Http.CookieSecurePolicy.SameAsRequest;
				}

				o.Scope.Clear();
				foreach (var s in auth.Oidc.Scopes)
					o.Scope.Add(s);

				o.TokenValidationParameters.NameClaimType = auth.Oidc.UsernameClaim;

				// 对外源(反代/多端口开发):构造正确的 redirect_uri,使回调与会话 cookie 落在浏览器实际访问的源。
				var publicOrigin = auth.Oidc.PublicOrigin?.TrimEnd('/');

				o.Events = new OpenIdConnectEvents
				{
					// 发起 challenge 时:若配置了对外源,覆盖 redirect_uri / post_logout_redirect_uri 的 host。
					OnRedirectToIdentityProvider = ctx =>
					{
						if (!string.IsNullOrEmpty(publicOrigin))
							ctx.ProtocolMessage.RedirectUri = publicOrigin + auth.Oidc.CallbackPath;
						return Task.CompletedTask;
					},
					// 登录成功后:根据 iss+sub 在本地建号或关联。
					OnTokenValidated = async ctx =>
					{
						var provisioner = ctx.HttpContext.RequestServices
							.GetRequiredService<IUserProvisioningService>();
						await provisioner.ProvisionFromPrincipalAsync(ctx.Principal!, ctx.HttpContext.RequestAborted);
					}
				};
			});
		}

		services.AddScoped<IUserProvisioningService, UserProvisioningService>();
		services.AddScoped<ICurrentUserAccessor, CurrentUserAccessor>();

		services.AddAuthorization();

		return services;
	}
}
