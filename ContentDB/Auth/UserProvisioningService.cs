// ContentDB C# —— OIDC 首次登录建号 / 关联

using System.Security.Claims;
using ContentDB.Core.Configuration;
using ContentDB.Core.Domain;
using ContentDB.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ContentDB.Api.Auth;

public interface IUserProvisioningService
{
	/// <summary>根据 OIDC 登录主体在本地建号或关联,并把本地用户信息写回 principal(补充 NameIdentifier=本地用户id)。</summary>
	Task<User> ProvisionFromPrincipalAsync(ClaimsPrincipal principal, CancellationToken ct = default);
}

public sealed class UserProvisioningService : IUserProvisioningService
{
	private readonly AppDbContext _db;
	private readonly AuthOptions _auth;
	private readonly ILogger<UserProvisioningService> _logger;

	public UserProvisioningService(AppDbContext db, IOptions<AuthOptions> auth, ILogger<UserProvisioningService> logger)
	{
		_db = db;
		_auth = auth.Value;
		_logger = logger;
	}

	public async Task<User> ProvisionFromPrincipalAsync(ClaimsPrincipal principal, CancellationToken ct = default)
	{
		var issuer = principal.FindFirstValue("iss")
			?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Issuer
			?? "";
		var subject = principal.FindFirstValue("sub")
			?? principal.FindFirstValue(ClaimTypes.NameIdentifier)
			?? throw new InvalidOperationException("OIDC principal missing subject");

		var user = await _db.Users
			.FirstOrDefaultAsync(u => u.OidcIssuer == issuer && u.OidcSubject == subject, ct);

		if (user is null)
		{
			var username = await DeriveUniqueUsernameAsync(principal, ct);
			bool isFirst = !await _db.Users.AnyAsync(ct);

			user = new User
			{
				Username = username,
				OidcIssuer = issuer,
				OidcSubject = subject,
				Rank = (isFirst && _auth.FirstUserIsAdmin) ? UserRank.ADMIN : UserRank.NEW_MEMBER,
				DisplayName = principal.FindFirstValue("name") ?? principal.FindFirstValue(_auth.Oidc.UsernameClaim),
				Email = principal.FindFirstValue("email"),
				ProfilePicUrl = principal.FindFirstValue("picture"),
				CreatedAt = DateTimeOffset.UtcNow,
				IsActive = true,
			};
			if (principal.FindFirstValue("email_verified") == "true")
				user.EmailConfirmedAt = DateTimeOffset.UtcNow;

			_db.Users.Add(user);
			await _db.SaveChangesAsync(ct);
			_logger.LogInformation("OIDC 首次登录建号: {Username} (rank={Rank})", user.Username, user.Rank);
		}
		else
		{
			// 同步资料(非破坏性)
			user.Email ??= principal.FindFirstValue("email");
			user.DisplayName ??= principal.FindFirstValue("name");

			// 自愈:早期登录曾把 username 存成了 OIDC sub(不友好的不透明 ID)。
			// 若当前 username 仍等于 sub,重新派生一个友好的用户名。
			if (string.Equals(user.Username, subject, StringComparison.Ordinal))
			{
				var friendly = await DeriveUniqueUsernameAsync(principal, ct);
				if (!string.Equals(friendly, subject, StringComparison.Ordinal))
				{
					_logger.LogInformation("修正用户名: {Old} -> {New}", user.Username, friendly);
					user.Username = friendly;
				}
			}

			await _db.SaveChangesAsync(ct);
		}

		// 把本地用户 id 写入 principal,便于 ICurrentUserAccessor 解析
		if (principal.Identity is ClaimsIdentity id)
		{
			var existing = id.FindFirst("cdb:uid");
			if (existing is not null) id.RemoveClaim(existing);
			id.AddClaim(new Claim("cdb:uid", user.Id.ToString()));
		}

		return user;
	}

	private async Task<string> DeriveUniqueUsernameAsync(ClaimsPrincipal principal, CancellationToken ct)
	{
		var subject = principal.FindFirstValue("sub") ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);

		// 优先友好的人类可读 claim;显式排除 sub(不透明 ID)。
		var raw = FirstNonSub(subject,
			principal.FindFirstValue(_auth.Oidc.UsernameClaim),
			principal.FindFirstValue("username"),
			principal.FindFirstValue("preferred_username"),
			principal.FindFirstValue("name"),
			principal.FindFirstValue("nickname"),
			principal.FindFirstValue("email")?.Split('@')[0])
			?? "user";

		// 规约为合法用户名字符
		var cleaned = new string(raw.Where(c => char.IsLetterOrDigit(c) || c is '.' or '_' or '-').ToArray());
		if (string.IsNullOrEmpty(cleaned)) cleaned = "user";

		var candidate = cleaned;
		int suffix = 1;
		while (await _db.Users.AnyAsync(u => u.Username.ToLower() == candidate.ToLower(), ct))
		{
			candidate = $"{cleaned}{suffix++}";
		}
		return candidate;
	}

	/// <summary>返回第一个非空、且不等于 OIDC sub 的候选值(避免把不透明 ID 当用户名)。</summary>
	private static string? FirstNonSub(string? subject, params string?[] candidates)
	{
		foreach (var c in candidates)
		{
			if (string.IsNullOrWhiteSpace(c)) continue;
			if (subject is not null && string.Equals(c, subject, StringComparison.Ordinal)) continue;
			return c;
		}
		return null;
	}
}
