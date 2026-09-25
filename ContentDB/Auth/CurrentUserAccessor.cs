// ContentDB C# —— 当前用户访问器
// 从 HttpContext.User(Cookie/OIDC 会话)或 Authorization: Bearer(API Token)解析出本地 User 实体。

using System.Security.Claims;
using ContentDB.Core.Domain;
using ContentDB.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;

namespace ContentDB.Api.Auth;

public interface ICurrentUserAccessor
{
	/// <summary>当前登录用户(未登录返回 null)。带常用关系加载(维护的包等按需)。</summary>
	Task<User?> GetAsync(CancellationToken ct = default);

	/// <summary>当前 API Token id(若通过 Bearer 认证)。</summary>
	int? GetTokenId();
}

public sealed class CurrentUserAccessor : ICurrentUserAccessor
{
	private readonly IHttpContextAccessor _http;
	private readonly AppDbContext _db;
	private User? _cached;
	private bool _resolved;
	private int? _tokenId;

	public CurrentUserAccessor(IHttpContextAccessor http, AppDbContext db)
	{
		_http = http;
		_db = db;
	}

	public async Task<User?> GetAsync(CancellationToken ct = default)
	{
		if (_resolved) return _cached;
		_resolved = true;

		var httpContext = _http.HttpContext;
		if (httpContext is null) return _cached = null;

		// 1) 默认方案(Cookie/OIDC 会话)
		var principal = httpContext.User;

		// 2) 若会话未认证,尝试 API Token(Bearer)方案
		if (principal?.Identity is not { IsAuthenticated: true })
		{
			var result = await httpContext.AuthenticateAsync(AuthenticationSetup.ApiTokenScheme);
			if (result.Succeeded)
				principal = result.Principal;
		}

		if (principal?.Identity is not { IsAuthenticated: true })
			return _cached = null;

		// token id(若来自 Bearer)
		var tokenVal = principal.FindFirstValue(ApiTokenAuthenticationHandler.TokenIdClaim);
		if (int.TryParse(tokenVal, out var tid)) _tokenId = tid;

		// 本地 uid(OIDC 建号后写入)或 API Token 的 NameIdentifier
		var uidStr = principal.FindFirstValue("cdb:uid")
			?? principal.FindFirstValue(ClaimTypes.NameIdentifier);

		if (int.TryParse(uidStr, out var uid))
		{
			_cached = await _db.Users
				.Include(u => u.MaintainedPackages)
				.FirstOrDefaultAsync(u => u.Id == uid, ct);
		}

		return _cached;
	}

	public int? GetTokenId() => _tokenId;
}
