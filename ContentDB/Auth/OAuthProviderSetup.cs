// ContentDB C# —— 本站作 OAuth2 提供方(OpenIddict server)
// 提供 authorization code + PKCE 流程给第三方应用(对应原站 OAuthClient 功能)。

using ContentDB.Infrastructure.Data;
using OpenIddict.Abstractions;

namespace ContentDB.Api.Auth;

public static class OAuthProviderSetup
{
	public static IServiceCollection AddContentDbOAuthProvider(this IServiceCollection services)
	{
		services.AddOpenIddict()
			.AddCore(options =>
			{
				options.UseEntityFrameworkCore()
					.UseDbContext<AppDbContext>();
			})
			.AddServer(options =>
			{
				options.SetAuthorizationEndpointUris("/oauth/authorize")
					.SetTokenEndpointUris("/oauth/token")
					.SetUserInfoEndpointUris("/oauth/userinfo")
					.SetEndSessionEndpointUris("/oauth/logout");

				options.AllowAuthorizationCodeFlow()
					.RequireProofKeyForCodeExchange()
					.AllowRefreshTokenFlow();

				options.RegisterScopes(
					OpenIddictConstants.Scopes.OpenId,
					OpenIddictConstants.Scopes.Profile,
					OpenIddictConstants.Scopes.Email,
					"public");

				// 开发用证书;生产应替换为持久化的签名/加密证书。
				options.AddDevelopmentEncryptionCertificate()
					.AddDevelopmentSigningCertificate();

				options.UseAspNetCore()
					.EnableAuthorizationEndpointPassthrough()
					.EnableTokenEndpointPassthrough()
					.EnableUserInfoEndpointPassthrough()
					.EnableEndSessionEndpointPassthrough();
			})
			.AddValidation(options =>
			{
				options.UseLocalServer();
				options.UseAspNetCore();
			});

		return services;
	}
}
