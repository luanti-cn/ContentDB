// ContentDB C# —— Luanti .conf 解析器
// 解析 mod.conf / game.conf / texture_pack.conf 的 key = value 格式,
// 支持多行 key = """ ... """ 块。对应原 Python 的 conf 解析逻辑。

namespace ContentDB.Core.Services;

public static class LuantiConfParser
{
	/// <summary>解析 .conf 文本为键值字典(键小写不敏感按原样保留,查询用忽略大小写)。</summary>
	public static Dictionary<string, string> Parse(string text)
	{
		var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		if (string.IsNullOrEmpty(text)) return result;

		// 规范化换行
		text = text.Replace("\r\n", "\n").Replace("\r", "\n");
		var lines = text.Split('\n');

		for (int i = 0; i < lines.Length; i++)
		{
			var line = lines[i];
			var trimmed = line.TrimStart();
			if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;

			var eq = line.IndexOf('=');
			if (eq < 0) continue;

			var key = line[..eq].Trim();
			var value = line[(eq + 1)..].Trim();

			// 多行块: key = """ ... """
			if (value.StartsWith("\"\"\""))
			{
				var sb = new System.Text.StringBuilder();
				var afterOpen = value[3..];
				// 同行即闭合
				var closeIdx = afterOpen.IndexOf("\"\"\"", StringComparison.Ordinal);
				if (closeIdx >= 0)
				{
					sb.Append(afterOpen[..closeIdx]);
				}
				else
				{
					sb.AppendLine(afterOpen);
					i++;
					for (; i < lines.Length; i++)
					{
						var l = lines[i];
						var ci = l.IndexOf("\"\"\"", StringComparison.Ordinal);
						if (ci >= 0)
						{
							sb.Append(l[..ci]);
							break;
						}
						sb.AppendLine(l);
					}
				}
				result[key] = sb.ToString().Trim('\n');
			}
			else
			{
				result[key] = value;
			}
		}

		return result;
	}

	/// <summary>解析依赖 spec:逗号分隔的 mod 名列表。</summary>
	public static List<string> ParseDependList(string? spec)
	{
		var list = new List<string>();
		if (string.IsNullOrWhiteSpace(spec)) return list;
		foreach (var raw in spec.Split(','))
		{
			var name = raw.Trim();
			if (name.Length == 0) continue;
			// 去掉可能的版本约束(取第一个非法字符前的部分)
			var clean = new string(name.TakeWhile(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
			if (clean.Length > 0) list.Add(clean);
		}
		return list;
	}
}
