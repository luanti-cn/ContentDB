// ContentDB C# —— 服务器账号保管库服务
// 每用户每服务器一条凭证;(UserId, Address) 唯一;密码 AES-256-GCM 加密落库。

using ContentDB.Core.Abstractions;
using ContentDB.Core.Configuration;
using ContentDB.Core.Domain;
using ContentDB.Core.Services;
using ContentDB.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ContentDB.Infrastructure.Services;

public sealed class CloudVaultService : ICloudVaultService
{
	private readonly AppDbContext _db;
	private readonly IVaultCrypto _crypto;
	private readonly VaultOptions _options;

	public CloudVaultService(AppDbContext db, IVaultCrypto crypto, IOptions<VaultOptions> options)
	{
		_db = db;
		_crypto = crypto;
		_options = options.Value;
	}

	public async Task<ServiceResult> SetDefaultUsernameAsync(User owner, string? name, CancellationToken ct = default)
	{
		name = name?.Trim() ?? "";
		if (name.Length == 0)
		{
			owner.DefaultServerUsername = null;
		}
		else
		{
			if (!LuantiAccountName.IsValid(name))
				return ServiceResult.Fail(400, "Invalid Luanti username (1-20 of A-Za-z0-9_-)");
			owner.DefaultServerUsername = name;
		}
		await _db.SaveChangesAsync(ct);
		return ServiceResult.Ok(new { default_server_username = owner.DefaultServerUsername ?? owner.Username });
	}

	public async Task<IReadOnlyList<VaultEntryInfo>> ListAsync(User owner, CancellationToken ct = default)
	{
		return await _db.ServerCredentials
			.Where(c => c.UserId == owner.Id)
			.OrderBy(c => c.Address)
			.Select(c => new VaultEntryInfo(c.Id, c.Address, c.Username, c.Status.ToString(), c.CreatedAt, c.UpdatedAt))
			.ToListAsync(ct);
	}

	public Task<ServiceResult> AddAsync(User owner, string address, string username, string password, CancellationToken ct = default)
		=> UpsertCoreAsync(owner, address, username, password, existingId: null, ct: ct);

	public async Task<ServiceResult> UpdateAsync(User owner, int id, string? username, string? password, CancellationToken ct = default)
	{
		var cred = await _db.ServerCredentials
			.FirstOrDefaultAsync(c => c.Id == id && c.UserId == owner.Id, ct);
		if (cred is null) return ServiceResult.Fail(404, "Credential not found");

		if (username is not null)
		{
			if (!LuantiAccountName.IsValid(username))
				return ServiceResult.Fail(400, "Invalid Luanti username (1-20 of A-Za-z0-9_-)");
			cred.Username = username;
		}
		if (password is not null)
		{
			if (password.Length is 0 or > 100)
				return ServiceResult.Fail(400, "password must be 1-100 chars");
			cred.PasswordEncrypted = _crypto.Encrypt(password);
		}
		cred.Status = CredentialStatus.ACTIVE;
		cred.InvalidReportedAt = null;
		cred.UpdatedAt = DateTimeOffset.UtcNow;
		await _db.SaveChangesAsync(ct);
		return ServiceResult.Ok(ToInfo(cred));
	}

	public async Task<ServiceResult> DeleteAsync(User owner, int id, CancellationToken ct = default)
	{
		var cred = await _db.ServerCredentials
			.FirstOrDefaultAsync(c => c.Id == id && c.UserId == owner.Id, ct);
		if (cred is null) return ServiceResult.Fail(404, "Credential not found");
		_db.ServerCredentials.Remove(cred);
		await _db.SaveChangesAsync(ct);
		return ServiceResult.Ok(new { success = true });
	}

	public async Task<ServiceResult> RevealAsync(User owner, int id, CancellationToken ct = default)
	{
		var cred = await _db.ServerCredentials
			.FirstOrDefaultAsync(c => c.Id == id && c.UserId == owner.Id, ct);
		if (cred is null) return ServiceResult.Fail(404, "Credential not found");
		return ServiceResult.Ok(ToSecret(cred, _crypto.Decrypt(cred.PasswordEncrypted)));
	}

	public async Task<ServiceResult> MarkInvalidAsync(User owner, string address, CancellationToken ct = default)
	{
		var normalized = ServerAddress.Normalize(address);
		if (normalized is null) return ServiceResult.Fail(400, "Invalid server address");

		var cred = await _db.ServerCredentials
			.FirstOrDefaultAsync(c => c.UserId == owner.Id && c.Address == normalized, ct);
		if (cred is null) return ServiceResult.Fail(404, "Credential not found");

		cred.Status = CredentialStatus.NEEDS_UPDATE;
		cred.InvalidReportedAt = DateTimeOffset.UtcNow;
		await _db.SaveChangesAsync(ct);
		return ServiceResult.Ok(new { success = true, status = cred.Status.ToString() });
	}

	public async Task<VaultSecret?> FindForServerAsync(User owner, string address, CancellationToken ct = default)
	{
		var normalized = ServerAddress.Normalize(address);
		if (normalized is null) return null;
		var cred = await _db.ServerCredentials
			.FirstOrDefaultAsync(c => c.UserId == owner.Id && c.Address == normalized, ct);
		return cred is null ? null : ToSecret(cred, _crypto.Decrypt(cred.PasswordEncrypted));
	}

	public Task<ServiceResult> UpsertForServerAsync(User owner, string address, string username, string password, CancellationToken ct = default)
		=> UpsertCoreAsync(owner, address, username, password, existingId: null, upsert: true, ct: ct);

	private async Task<ServiceResult> UpsertCoreAsync(
		User owner, string address, string username, string password,
		int? existingId, bool upsert = false, CancellationToken ct = default)
	{
		var normalized = ServerAddress.Normalize(address);
		if (normalized is null)
			return ServiceResult.Fail(400, "Invalid server address");

		if (!LuantiAccountName.IsValid(username))
			return ServiceResult.Fail(400, "Invalid Luanti username (1-20 of A-Za-z0-9_-)");
		if (string.IsNullOrEmpty(password) || password.Length > 100)
			return ServiceResult.Fail(400, "password must be 1-100 chars");

		var cred = await _db.ServerCredentials
			.FirstOrDefaultAsync(c => c.UserId == owner.Id && c.Address == normalized, ct);

		if (cred is not null && existingId is not null && cred.Id != existingId)
			return ServiceResult.Fail(409, "Another entry already exists for this address");

		if (cred is not null)
		{
			cred.Username = username;
			cred.PasswordEncrypted = _crypto.Encrypt(password);
			cred.Status = CredentialStatus.ACTIVE;
			cred.InvalidReportedAt = null;
			cred.UpdatedAt = DateTimeOffset.UtcNow;
			await _db.SaveChangesAsync(ct);
			return ServiceResult.Ok(ToInfo(cred));
		}

		if (!upsert)
		{
			var count = await _db.ServerCredentials.CountAsync(c => c.UserId == owner.Id, ct);
			var max = Math.Max(1, _options.MaxVaultEntriesPerUser);
			if (count >= max)
				return ServiceResult.Fail(409, $"Vault entry limit reached ({max})");
		}

		cred = new ServerCredential
		{
			UserId = owner.Id,
			Address = normalized,
			Username = username,
			PasswordEncrypted = _crypto.Encrypt(password),
			CreatedAt = DateTimeOffset.UtcNow,
			UpdatedAt = DateTimeOffset.UtcNow,
		};
		_db.ServerCredentials.Add(cred);
		await _db.SaveChangesAsync(ct);
		return ServiceResult.Ok(ToInfo(cred));
	}

	private static VaultEntryInfo ToInfo(ServerCredential c)
		=> new(c.Id, c.Address, c.Username, c.Status.ToString(), c.CreatedAt, c.UpdatedAt);

	private static VaultSecret ToSecret(ServerCredential c, string password)
		=> new(c.Id, c.Address, c.Username, password, c.UpdatedAt);
}
