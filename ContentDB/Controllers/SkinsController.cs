// ContentDB C# —— 皮肤分发:按角色名(即游戏内用户名)提供 Luanti 皮肤 PNG。
// 供服务端 companion mod(如 cloud_skins)在玩家进服时拉取并应用。匿名可读。

using System.Text.RegularExpressions;
using ContentDB.Core.Abstractions;
using ContentDB.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ContentDB.Api.Controllers;

[ApiController]
public sealed class SkinsController : ControllerBase
{
	private static readonly Regex NamePattern = new("^[A-Za-z0-9_-]{1,20}$", RegexOptions.Compiled);

	private readonly AppDbContext _db;
	private readonly IObjectStorage _storage;
	private readonly ILogger<SkinsController> _logger;

	public SkinsController(AppDbContext db, IObjectStorage storage, ILogger<SkinsController> logger)
	{
		_db = db;
		_storage = storage;
		_logger = logger;
	}

	/// <summary>
	/// GET /skins/{name}.png —— 返回角色名对应的皮肤 PNG(Luanti 皮肤布局)。
	/// 同名角色可能属于不同用户:取 UpdatedAt 最新者(与"同名 = 同一公开形象"的直觉一致)。
	/// 无角色 / 无皮肤 / 对象缺失一律 404,调用方(服务端 mod)应保持默认外观。
	/// 支持 ETag/304;Cache-Control 允许短时缓存。
	/// </summary>
	[HttpGet("/skins/{name}.png")]
	public async Task<IActionResult> Get(string name, CancellationToken ct)
	{
		if (!NamePattern.IsMatch(name))
			return NotFound();

		var lower = name.ToLowerInvariant();
		var character = await _db.PlayerCharacters
			.Where(c => c.Name == name || c.Name.ToLower() == lower)
			.OrderByDescending(c => c.UpdatedAt)
			.FirstOrDefaultAsync(ct);
		if (character?.SkinUrl is not string skinUrl || skinUrl == "")
			return NotFound();

		// 外链皮肤:直接 302(mod 的 HTTP 客户端会跟随重定向)。
		if (skinUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
			skinUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
			return Redirect(skinUrl);

		// 本地上传:/uploads/<file> → 对象存储 key "uploads/<file>"。
		if (!skinUrl.StartsWith("/uploads/", StringComparison.Ordinal))
			return NotFound();
		var key = skinUrl.TrimStart('/');

		try
		{
			var info = await _storage.GetInfoAsync(key, ct);
			if (info is null)
				return NotFound();

			var etag = $"\"skin-{character.Id}-{character.UpdatedAt.Ticks}\"";
			if (Request.Headers.IfNoneMatch.ToString().Contains(etag))
				return StatusCode(304);

			var stream = await _storage.OpenReadAsync(key, ct);
			if (stream is null)
				return NotFound();

			Response.Headers.CacheControl = "public, max-age=300";
			Response.Headers.ETag = etag;
			return File(stream, info.ContentType ?? "image/png");
		}
		catch (OperationCanceledException) when (ct.IsCancellationRequested)
		{
			return new EmptyResult();
		}
		catch (Exception ex)
		{
			// 对象存储抖动不 500,降级 404(mod 保持默认外观即可)。
			_logger.LogWarning(ex, "读取皮肤对象失败 {Key}", key);
			return NotFound();
		}
	}
}
