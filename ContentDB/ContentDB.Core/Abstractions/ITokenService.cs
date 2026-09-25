// ContentDB C# —— API Token 管理服务抽象

using ContentDB.Core.Domain;

namespace ContentDB.Core.Abstractions;

public sealed record CreatedToken(int Id, string Name, string AccessToken);

public sealed record TokenInfo(int Id, string Name, DateTimeOffset CreatedAt, string? PackageKey);

public interface ITokenService
{
	/// <summary>列出用户的 token(不含明文,仅元信息)。</summary>
	Task<IReadOnlyList<TokenInfo>> ListAsync(User owner, CancellationToken ct = default);

	/// <summary>创建 token,返回一次性明文(仅此次可见)。</summary>
	Task<ServiceResult> CreateAsync(User owner, string name, string? packageAuthor, string? packageName, CancellationToken ct = default);

	/// <summary>删除 token。</summary>
	Task<ServiceResult> DeleteAsync(User owner, int tokenId, CancellationToken ct = default);
}
