// ContentDB C# —— API Token 管理服务实现

using System.Security.Cryptography;
using ContentDB.Core.Abstractions;
using ContentDB.Core.Domain;
using ContentDB.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContentDB.Infrastructure.Services;

public sealed class TokenService : ITokenService
{
	private readonly AppDbContext _db;

	public TokenService(AppDbContext db) => _db = db;

	public async Task<IReadOnlyList<TokenInfo>> ListAsync(User owner, CancellationToken ct = default)
	{
		return await _db.ApiTokens
			.Where(t => t.OwnerId == owner.Id)
			.Include(t => t.Package).ThenInclude(p => p!.Author)
			.OrderByDescending(t => t.CreatedAt)
			.Select(t => new TokenInfo(
				t.Id, t.Name, t.CreatedAt,
				t.Package != null ? t.Package.Author.Username + "/" + t.Package.Name : null))
			.ToListAsync(ct);
	}

	public async Task<ServiceResult> CreateAsync(User owner, string name, string? packageAuthor, string? packageName, CancellationToken ct = default)
	{
		if (!owner.Rank.AtLeast(UserRank.NEW_MEMBER))
			return ServiceResult.Fail(403, "Insufficient rank to create tokens");
		if (string.IsNullOrWhiteSpace(name))
			return ServiceResult.Fail(400, "name is required");

		int? packageId = null;
		if (!string.IsNullOrEmpty(packageAuthor) && !string.IsNullOrEmpty(packageName))
		{
			packageId = await _db.Packages
				.Where(p => p.Author.Username == packageAuthor && p.Name == packageName)
				.Select(p => (int?)p.Id).FirstOrDefaultAsync(ct);
			if (packageId is null)
				return ServiceResult.Fail(404, "Package not found");
		}

		var accessToken = GenerateToken();
		var token = new APIToken
		{
			Name = name,
			AccessToken = accessToken,
			OwnerId = owner.Id,
			PackageId = packageId,
			CreatedAt = DateTimeOffset.UtcNow,
		};
		_db.ApiTokens.Add(token);
		await _db.SaveChangesAsync(ct);

		// 明文仅此次返回
		return ServiceResult.Ok(new CreatedToken(token.Id, token.Name, accessToken));
	}

	public async Task<ServiceResult> DeleteAsync(User owner, int tokenId, CancellationToken ct = default)
	{
		var token = await _db.ApiTokens.FirstOrDefaultAsync(t => t.Id == tokenId && t.OwnerId == owner.Id, ct);
		if (token is null) return ServiceResult.Fail(404, "Token not found");
		_db.ApiTokens.Remove(token);
		await _db.SaveChangesAsync(ct);
		return ServiceResult.Ok(new { success = true });
	}

	private static string GenerateToken()
	{
		// 48 字节 -> base64url,足够长且不含分隔符
		Span<byte> bytes = stackalloc byte[48];
		RandomNumberGenerator.Fill(bytes);
		return Convert.ToBase64String(bytes)
			.Replace('+', '-').Replace('/', '_').TrimEnd('=');
	}
}
