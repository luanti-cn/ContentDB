// ContentDB C# —— 参考/查找类实体
// License / Tag / ContentWarning / MetaPackage / LuantiRelease / Language

namespace ContentDB.Core.Domain;

public class License
{
	public int Id { get; set; }
	public string Name { get; set; } = "";
	public bool IsFoss { get; set; } = true;
	public string? Url { get; set; }

	public ICollection<Package> Packages { get; set; } = new List<Package>();
}

public class Tag
{
	public int Id { get; set; }
	public string Name { get; set; } = "";
	public string Title { get; set; } = "";
	public string? Description { get; set; }
	public string BackgroundColor { get; set; } = "000000";
	public string TextColor { get; set; } = "ffffff";
	public int Views { get; set; }

	public ICollection<Package> Packages { get; set; } = new List<Package>();
}

public class ContentWarning
{
	public int Id { get; set; }
	public string Name { get; set; } = "";
	public string Title { get; set; } = "";
	public string Description { get; set; } = "";

	public ICollection<Package> Packages { get; set; } = new List<Package>();
}

/// <summary>虚拟依赖名,多个包可"提供"(provides)它。</summary>
public class MetaPackage
{
	public int Id { get; set; }
	public string Name { get; set; } = "";

	public ICollection<Package> Packages { get; set; } = new List<Package>();
	public ICollection<Dependency> Dependencies { get; set; } = new List<Dependency>();
}

/// <summary>引擎版本注册表(name + protocol)。</summary>
public class LuantiRelease
{
	public int Id { get; set; }
	public string Name { get; set; } = "";
	public int Protocol { get; set; }

	public bool IsDev => Name.Contains("-dev");

	/// <summary>"None" 版本视为无约束。</summary>
	public LuantiRelease? GetActual() => Name == "None" ? null : this;
}

public class Language
{
	public string Id { get; set; } = "";  // 如 "zh", "en"
	public string Title { get; set; } = "";
}
