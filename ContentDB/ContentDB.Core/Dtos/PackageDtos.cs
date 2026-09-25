// ContentDB C# Mirror
// 只读镜像 DTO —— 与官方 contentdb JSON 契约 1:1 对齐。
// 字段名通过 [JsonPropertyName] 精确匹配官方输出,便于客户端无感切换。

using System.Text.Json;
using System.Text.Json.Serialization;

namespace ContentDB.Core.Dtos;

/// <summary>
/// 官方 /api/packages/ 列表项(短字典 as_short_dict / convert_to_dictionary 输出)。
/// 为了忠实转发官方响应,列表与详情等端点在镜像里主要以透传 JSON 为主;
/// 该 DTO 用于需要在本地读取/改写字段(如 thumbnail、release)的场景。
/// </summary>
public sealed class PackageShortDto
{
	[JsonPropertyName("name")]
	public string Name { get; set; } = "";

	[JsonPropertyName("title")]
	public string Title { get; set; } = "";

	[JsonPropertyName("author")]
	public string Author { get; set; } = "";

	[JsonPropertyName("short_description")]
	public string? ShortDescription { get; set; }

	[JsonPropertyName("type")]
	public string Type { get; set; } = "";

	[JsonPropertyName("release")]
	public int? Release { get; set; }

	[JsonPropertyName("thumbnail")]
	public string? Thumbnail { get; set; }

	[JsonPropertyName("aliases")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public List<string>? Aliases { get; set; }

	[JsonPropertyName("repo")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Repo { get; set; }

	[JsonPropertyName("featured")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public bool? Featured { get; set; }
}

/// <summary>
/// 官方 release as_dict / as_long_dict 输出。
/// </summary>
public sealed class ReleaseDto
{
	[JsonPropertyName("id")]
	public int Id { get; set; }

	[JsonPropertyName("name")]
	public string Name { get; set; } = "";

	[JsonPropertyName("title")]
	public string Title { get; set; } = "";

	[JsonPropertyName("release_notes")]
	public string? ReleaseNotes { get; set; }

	[JsonPropertyName("url")]
	public string? Url { get; set; }

	[JsonPropertyName("release_date")]
	public string ReleaseDate { get; set; } = "";

	[JsonPropertyName("commit")]
	public string? Commit { get; set; }

	[JsonPropertyName("downloads")]
	public int Downloads { get; set; }

	[JsonPropertyName("min_minetest_version")]
	public JsonElement? MinMinetestVersion { get; set; }

	[JsonPropertyName("max_minetest_version")]
	public JsonElement? MaxMinetestVersion { get; set; }

	[JsonPropertyName("size")]
	public long Size { get; set; }
}
