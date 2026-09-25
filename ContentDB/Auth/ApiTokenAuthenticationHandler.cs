// ContentDB C# —— API Token(Bearer)认证处理器
// 移植原 api/auth.py 的 is_api_authd:Authorization: Bearer <token>,查 APIToken 表。

using System.Security.Claims;
using System.Text.Encodings.Web;
using ContentDB.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ContentDB.Api.Auth;

public sealed class ApiTokenAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
	public const string TokenIdClaim = "cdb:token_id";

	private readonly AppDbContext _db;

	public ApiTokenAuthenticationHandler(
		IOptionsMonitor<AuthenticationSchemeOptions> options,
		ILoggerFactory logger,
		UrlEncoder encoder,
		AppDbContext db) : base(options, logger, encoder)
	{
		_db = db;
	}

	protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
	{
		var header = Request.Headers.Authorization.ToString();
		if (string.IsNullOrEmpty(header))
			return AuthenticateResult.NoResult();  // 不阻止匿名读端点

		if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
			return AuthenticateResult.Fail("Unsupported authentication method");

		var accessToken = header[7..].Trim();
		if (accessToken.Length < 10)
			return AuthenticateResult.Fail("API token is too short");

		var token = await _db.ApiTokens
			.Include(t => t.Owner)
			.FirstOrDefaultAsync(t => t.AccessToken == accessToken);

		if (token is null)
			return AuthenticateResult.Fail("Unknown API token");

		var identity = new ClaimsIdentity(AuthenticationSetup.ApiTokenScheme);
		identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, token.Owner.Id.ToString()));
		identity.AddClaim(new Claim(ClaimTypes.Name, token.Owner.Username));
		identity.AddClaim(new Claim(TokenIdClaim, token.Id.ToString()));

		var principal = new ClaimsPrincipal(identity);
		var ticket = new AuthenticationTicket(principal, AuthenticationSetup.ApiTokenScheme);
		return AuthenticateResult.Success(ticket);
	}
}
