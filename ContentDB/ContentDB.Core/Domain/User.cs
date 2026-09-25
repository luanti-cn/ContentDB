// ContentDB C# —— User 实体
// 从原 Python User 模型移植。认证方式改为外部 OIDC:去除本地密码,新增 OIDC subject 绑定。

namespace ContentDB.Core.Domain;

public class User
{
	public int Id { get; set; }

	/// <summary>用户名(大小写不敏感唯一)。规则:^[A-Za-z0-9._-]+$。</summary>
	public string Username { get; set; } = "";

	public UserRank Rank { get; set; } = UserRank.NOT_JOINED;

	// ---- OIDC 身份绑定(替代原密码 / GitHub 登录)----
	/// <summary>OIDC 颁发者(iss),配合 Subject 唯一确定一个外部身份。</summary>
	public string? OidcIssuer { get; set; }

	/// <summary>OIDC 主体标识(sub)。</summary>
	public string? OidcSubject { get; set; }

	// ---- 云同步 ----
	/// <summary>进新游戏服务器时使用的默认游戏内用户名(空则回落到 Username)。</summary>
	public string? DefaultServerUsername { get; set; }

	// ---- 资料 ----
	public string? DisplayName { get; set; }
	public string? Email { get; set; }
	public DateTimeOffset? EmailConfirmedAt { get; set; }
	public string? Locale { get; set; }
	public string? ProfilePicUrl { get; set; }
	public bool IsActive { get; set; } = true;

	public string? WebsiteUrl { get; set; }
	public string? DonateUrl { get; set; }

	// ---- 遗留账号关联(用于认领/展示,可空)----
	public string? GithubUsername { get; set; }
	public string? ForumsUsername { get; set; }

	public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

	// ---- 关系 ----
	public ICollection<Package> Packages { get; set; } = new List<Package>();
	public ICollection<Package> MaintainedPackages { get; set; } = new List<Package>();
	public ICollection<APIToken> Tokens { get; set; } = new List<APIToken>();
	public ICollection<Collection> Collections { get; set; } = new List<Collection>();
	public ICollection<OAuthClient> OAuthClients { get; set; } = new List<OAuthClient>();
	public ICollection<Notification> Notifications { get; set; } = new List<Notification>();
	public ICollection<PairedDevice> PairedDevices { get; set; } = new List<PairedDevice>();
	public ICollection<ServerCredential> ServerCredentials { get; set; } = new List<ServerCredential>();
	public ICollection<PlayerCharacter> Characters { get; set; } = new List<PlayerCharacter>();

	public bool IsBanned => Rank == UserRank.BANNED;

	public string GetProfileUrl() => $"/users/{Username}/";

	/// <summary>用户级权限判定(移植自原 User.check_perm)。</summary>
	public bool CheckPerm(User? actor, Permission perm)
	{
		if (actor is null) return false;
		if (actor.IsBanned) return false;

		bool isSelf = actor.Id == Id;

		return perm switch
		{
			Permission.CHANGE_EMAIL or Permission.CHANGE_PROFILE_URLS or Permission.CHANGE_DISPLAY_NAME
				=> isSelf || actor.Rank.AtLeast(UserRank.MODERATOR),
			Permission.CHANGE_USERNAMES or Permission.CHANGE_RANK
				=> actor.Rank.AtLeast(UserRank.MODERATOR),
			Permission.CREATE_TOKEN
				=> isSelf && actor.Rank.AtLeast(UserRank.NEW_MEMBER),
			Permission.CREATE_OAUTH_CLIENT
				=> isSelf && actor.Rank.AtLeast(UserRank.MEMBER),
			Permission.APPROVE_OAUTH_CLIENT
				=> actor.Rank.AtLeast(UserRank.ADMIN),
			_ => throw new InvalidOperationException($"Permission {perm} is not related to users"),
		};
	}
}
