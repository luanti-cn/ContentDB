// ContentDB C# —— 设备 Token(Bearer)认证处理器
// 客户端配对后持有 device token;此处验 token 哈希、定位设备与用户,
// 并把 PairedDevice(含 User)放进 HttpContext.Items 供控制器直接取用。

using System.Security.Claims;
using System.Text.Encodings.Web;
using ContentDB.Core.Domain;
using ContentDB.Infrastructure.Data;
using ContentDB.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ContentDB.Api.Auth;

public sealed class DeviceTokenAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
	public const string DeviceIdClaim = "cdb:device_id";
	public const string DeviceItemKey = "cdb:paired_device";

	private readonly AppDbContext _db;

	public DeviceTokenAuthenticationHandler(
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
			return AuthenticateResult.NoResult();

		if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
			return AuthenticateResult.Fail("Unsupported authentication method");

		var token = header[7..].Trim();
		if (token.Length < 20)
			return AuthenticateResult.Fail("Invalid device token");

		var device = await _db.PairedDevices
			.Include(d => d.User)
			.FirstOrDefaultAsync(d => d.TokenHash == DevicePairingService.HashToken(token));

		if (device is null || !device.IsActive)
			return AuthenticateResult.Fail("Unknown or revoked device token");

		// 节流更新最后使用时间(至少间隔 60s 才写库)
		if ((device.LastUsedAt ?? DateTimeOffset.MinValue) < DateTimeOffset.UtcNow - TimeSpan.FromSeconds(60))
		{
			device.LastUsedAt = DateTimeOffset.UtcNow;
			await _db.SaveChangesAsync(Context.RequestAborted);
		}

		Context.Items[DeviceItemKey] = device;

		var identity = new ClaimsIdentity(AuthenticationSetup.DeviceTokenScheme);
		identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, device.UserId.ToString()));
		identity.AddClaim(new Claim(ClaimTypes.Name, device.User.Username));
		identity.AddClaim(new Claim(DeviceIdClaim, device.Id.ToString()));

		var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), AuthenticationSetup.DeviceTokenScheme);
		return AuthenticateResult.Success(ticket);
	}
}
