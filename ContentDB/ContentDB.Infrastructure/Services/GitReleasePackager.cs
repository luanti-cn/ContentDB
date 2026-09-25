// ContentDB C# —— git 打包器
// 用 LibGit2Sharp 克隆仓库到指定 ref,把工作树打包为 zip(排除 .git),上传对象存储。
// 对应原 Python 的 make_vcs_release(GitArchiver)。

using System.IO.Compression;
using ContentDB.Core.Abstractions;
using LibGit2Sharp;
using Microsoft.Extensions.Logging;

namespace ContentDB.Infrastructure.Services;

public sealed record GitPackResult(bool Success, string? ObjectKey, long SizeBytes, string? CommitHash, string? Error);

public interface IGitReleasePackager
{
	/// <summary>克隆 repo@ref -> 打包 zip -> 上传对象存储,返回对象 key。</summary>
	Task<GitPackResult> PackAsync(string repoUrl, string gitRef, string packageName, CancellationToken ct = default);
}

public sealed class GitReleasePackager : IGitReleasePackager
{
	private const long MaxZipBytes = 100L * 1024 * 1024; // 100 MB
	private static readonly char[] RandomChars = "abcdefghijklmnopqrstuvwxyz0123456789".ToCharArray();

	private readonly IObjectStorage _storage;
	private readonly ILogger<GitReleasePackager> _logger;

	public GitReleasePackager(IObjectStorage storage, ILogger<GitReleasePackager> logger)
	{
		_storage = storage;
		_logger = logger;
	}

	public async Task<GitPackResult> PackAsync(string repoUrl, string gitRef, string packageName, CancellationToken ct = default)
	{
		var workDir = Path.Combine(Path.GetTempPath(), "cdb-git", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(workDir);
		try
		{
			string commit;
			try
			{
				// 浅克隆后 checkout 指定 ref。LibGit2Sharp 不支持 shallow,常规 clone。
				Repository.Clone(repoUrl, workDir);
				using var repo = new Repository(workDir);
				var obj = repo.Lookup(gitRef) ?? (object?)repo.Branches[gitRef] ?? repo.Tags[gitRef]?.Target;
				if (obj is Branch br)
					Commands.Checkout(repo, br);
				else if (repo.Lookup<Commit>(gitRef) is { } c)
					Commands.Checkout(repo, c);
				else if (repo.Branches[$"origin/{gitRef}"] is { } rb)
					Commands.Checkout(repo, rb);
				else if (repo.Tags[gitRef] is { } tag && tag.Target is Commit tc)
					Commands.Checkout(repo, tc);

				commit = repo.Head.Tip?.Sha ?? gitRef;
			}
			catch (Exception ex)
			{
				_logger.LogWarning(ex, "git 克隆/checkout 失败 {Repo}@{Ref}", repoUrl, gitRef);
				return new GitPackResult(false, null, 0, null, $"git error: {ex.Message}");
			}

			// 打包 zip(排除 .git)
			var zipPath = Path.Combine(Path.GetTempPath(), $"cdb-{Guid.NewGuid():N}.zip");
			try
			{
				CreateZipExcludingGit(workDir, zipPath);
				var size = new FileInfo(zipPath).Length;
				if (size > MaxZipBytes)
					return new GitPackResult(false, null, size, commit, "Packaged zip exceeds 100 MB limit");

				var key = $"uploads/{Sanitize(packageName)}_{RandomString(10)}.zip";
				await using (var fs = File.OpenRead(zipPath))
					await _storage.PutAsync(key, fs, "application/zip", ct);

				_logger.LogInformation("git 打包完成 {Repo}@{Ref} -> {Key} ({Size} bytes)", repoUrl, gitRef, key, size);
				return new GitPackResult(true, key, size, commit, null);
			}
			finally
			{
				TryDelete(zipPath);
			}
		}
		finally
		{
			TryDeleteDir(workDir);
		}
	}

	private static void CreateZipExcludingGit(string sourceDir, string zipPath)
	{
		using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
		var baseUri = new Uri(sourceDir.EndsWith(Path.DirectorySeparatorChar) ? sourceDir : sourceDir + Path.DirectorySeparatorChar);

		foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
		{
			// 排除 .git 目录
			var rel = Uri.UnescapeDataString(baseUri.MakeRelativeUri(new Uri(file)).ToString());
			if (rel.StartsWith(".git/") || rel.Contains("/.git/") || rel == ".git")
				continue;

			zip.CreateEntryFromFile(file, rel, CompressionLevel.Optimal);
		}
	}

	private static string Sanitize(string s)
	{
		var chars = s.Select(c => char.IsLetterOrDigit(c) || c is '_' or '-' ? c : '_').ToArray();
		return new string(chars);
	}

	private static string RandomString(int len)
	{
		var chars = new char[len];
		var rnd = Random.Shared;
		for (int i = 0; i < len; i++) chars[i] = RandomChars[rnd.Next(RandomChars.Length)];
		return new string(chars);
	}

	private static void TryDelete(string path)
	{
		try { if (File.Exists(path)) File.Delete(path); } catch { /* ignore */ }
	}

	private static void TryDeleteDir(string dir)
	{
		try
		{
			if (!Directory.Exists(dir)) return;
			// .git 下有只读文件,先清只读属性
			foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
				File.SetAttributes(f, FileAttributes.Normal);
			Directory.Delete(dir, recursive: true);
		}
		catch { /* ignore */ }
	}
}
