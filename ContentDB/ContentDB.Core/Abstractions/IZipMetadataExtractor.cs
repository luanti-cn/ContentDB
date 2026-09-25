// ContentDB C# —— zip 发布内容解析
// 从上传的 zip 中读取 mod.conf/game.conf/texture_pack.conf,提取:
// 包类型推断、标题/描述、depends/optional_depends、provides(mod 名)。
// 对应原 Python 的 check_zip_release 元数据解析部分(不含病毒/安全深检)。

using System.IO.Compression;
using ContentDB.Core.Domain;
using ContentDB.Core.Services;

namespace ContentDB.Core.Abstractions;

public sealed record ZipMetadata(
	PackageType? DetectedType,
	string? Title,
	string? Description,
	List<string> Depends,
	List<string> OptionalDepends,
	List<string> Provides,
	string? Error);

public interface IZipMetadataExtractor
{
	/// <summary>解析 zip 流,提取 Luanti 包元数据。zip 已在内存/可 seek。</summary>
	ZipMetadata Extract(Stream zipStream);
}

public sealed class ZipMetadataExtractor : IZipMetadataExtractor
{
	public ZipMetadata Extract(Stream zipStream)
	{
		try
		{
			using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: true);

			// 找到最浅层的 game.conf / mod.conf / texture_pack.conf / modpack.conf
			var confEntry = FindConf(archive, "game.conf", out var isGame)
				?? FindConf(archive, "texture_pack.conf", out var isTxp)
				?? FindConf(archive, "modpack.conf", out _)
				?? FindConf(archive, "mod.conf", out _);

			PackageType? type = null;
			if (confEntry is not null)
			{
				var lower = confEntry.FullName.ToLowerInvariant();
				if (lower.EndsWith("game.conf")) type = PackageType.GAME;
				else if (lower.EndsWith("texture_pack.conf")) type = PackageType.TXP;
				else type = PackageType.MOD;
			}

			if (confEntry is null)
				return new ZipMetadata(null, null, null, new(), new(), new(), null);

			using var reader = new StreamReader(confEntry.Open());
			var text = reader.ReadToEnd();
			var conf = LuantiConfParser.Parse(text);

			conf.TryGetValue("title", out var title);
			conf.TryGetValue("name", out var modName);
			conf.TryGetValue("description", out var desc);
			conf.TryGetValue("depends", out var depends);
			conf.TryGetValue("optional_depends", out var optDepends);

			var provides = new List<string>();
			if (!string.IsNullOrWhiteSpace(modName))
				provides.Add(modName.Trim());

			return new ZipMetadata(
				type,
				string.IsNullOrWhiteSpace(title) ? null : title.Trim(),
				string.IsNullOrWhiteSpace(desc) ? null : desc.Trim(),
				LuantiConfParser.ParseDependList(depends),
				LuantiConfParser.ParseDependList(optDepends),
				provides,
				null);
		}
		catch (InvalidDataException)
		{
			return new ZipMetadata(null, null, null, new(), new(), new(), "Invalid zip archive");
		}
		catch (Exception ex)
		{
			return new ZipMetadata(null, null, null, new(), new(), new(), ex.Message);
		}
	}

	/// <summary>找到路径最浅的指定 conf 文件(顶层或单层子目录)。</summary>
	private static ZipArchiveEntry? FindConf(ZipArchive archive, string fileName, out bool found)
	{
		found = false;
		ZipArchiveEntry? best = null;
		int bestDepth = int.MaxValue;

		foreach (var entry in archive.Entries)
		{
			if (!entry.FullName.EndsWith(fileName, StringComparison.OrdinalIgnoreCase)) continue;
			// 仅匹配文件名部分
			var slash = entry.FullName.TrimEnd('/').LastIndexOf('/');
			var pureName = slash >= 0 ? entry.FullName[(slash + 1)..] : entry.FullName;
			if (!pureName.Equals(fileName, StringComparison.OrdinalIgnoreCase)) continue;

			var depth = entry.FullName.Count(c => c == '/');
			if (depth < bestDepth)
			{
				bestDepth = depth;
				best = entry;
			}
		}

		found = best is not null;
		return best;
	}
}
