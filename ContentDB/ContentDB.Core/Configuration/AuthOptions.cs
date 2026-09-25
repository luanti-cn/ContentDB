// ContentDB C# —— 认证配置
// 外部 OIDC 登录(兼容 Casdoor/Logto/Keycloak 等标准 OIDC)+ 本站 OAuth2 提供方。

namespace ContentDB.Core.Configuration;

public sealed class AuthOptions
{
	public const string SectionName = "Auth";

	/// <summary>外部 OIDC 登录配置。</summary>
	public OidcOptions Oidc { get; set; } = new();

	/// <summary>首个登录的用户是否自动授予 ADMIN(便于初始化)。</summary>
	public bool FirstUserIsAdmin { get; set; } = true;
}

public sealed class OidcOptions
{
	/// <summary>是否启用外部 OIDC 登录。</summary>
	public bool Enabled { get; set; } = true;

	/// <summary>OIDC 颁发者地址(Authority),如 https://sso.example.com 或 Casdoor/Logto 的 issuer。</summary>
	public string Authority { get; set; } = "";

	public string ClientId { get; set; } = "";
	public string ClientSecret { get; set; } = "";

	/// <summary>额外请求的 scope(默认包含 openid profile email)。</summary>
	public List<string> Scopes { get; set; } = new() { "openid", "profile", "email" };

	/// <summary>用作用户名的声明名(默认 preferred_username;Casdoor/Logto 可能是 name 或 sub)。</summary>
	public string UsernameClaim { get; set; } = "preferred_username";

	/// <summary>是否要求 HTTPS 元数据(本地测试对接 http IdP 时可关)。</summary>
	public bool RequireHttpsMetadata { get; set; } = true;

	/// <summary>回调路径。</summary>
	public string CallbackPath { get; set; } = "/signin-oidc";

	/// <summary>
	/// 对外访问的源(scheme://host[:port]),用于在反代/多端口开发下构造正确的 OIDC redirect_uri。
	/// 例如开发期前端 http://localhost:3000,生产为 https://contentdb.example.com。
	/// 留空表示按请求自身的 host 构造(直连后端时适用)。
	/// </summary>
	public string PublicOrigin { get; set; } = "";

	/// <summary>是否使用 PAR(Pushed Authorization Requests)。部分 IdP/配置下 PAR 会报错,可关闭回退到标准前端跳转。</summary>
	public bool UsePushedAuthorization { get; set; } = false;

	/// <summary>
	/// 应用对外是否走 HTTPS。决定认证 cookie(会话/correlation/nonce)是否强制 Secure。
	/// 开发期用 http://localhost 访问时设为 false,否则浏览器会丢弃 Secure cookie 导致回调 correlation 失败。
	/// 与 RequireHttpsMetadata(IdP 元数据是否 HTTPS)相互独立。
	/// </summary>
	public bool SecureCookies { get; set; } = true;
}
