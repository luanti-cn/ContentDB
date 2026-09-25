// ContentDB C# —— Minecraft / Luanti 皮肤格式互相转换
// Luanti 默认角色模型的 64x32 贴图布局与老版 Minecraft 皮肤完全一致:
//   头(0,0,32,16) 帽(32,0,32,16) 身(16,16,24,16) 右臂(40,16,16,16) 右腿(0,16,16,16)
// 现代 MC 64x64 在此之上增加:左臂(32,48) 左腿(16,48) 与覆盖层
//   外套(16,32,24,16) 右袖(40,32,16,16) 左袖(48,48,16,16) 右裤(0,32,16,16) 左裤(0,48,16,16)

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace ContentDB.Infrastructure.Services;

public static class SkinConverter
{
	private static readonly Rectangle BodyBase = new(16, 16, 24, 16);
	private static readonly Rectangle RightArmBase = new(40, 16, 16, 16);
	private static readonly Rectangle RightLegBase = new(0, 16, 16, 16);
	private static readonly Rectangle LeftArmMc = new(32, 48, 16, 16);
	private static readonly Rectangle LeftLegMc = new(16, 48, 16, 16);
	private static readonly Rectangle JacketMc = new(16, 32, 24, 16);
	private static readonly Rectangle RightSleeveMc = new(40, 32, 16, 16);
	private static readonly Rectangle RightPantsMc = new(0, 32, 16, 16);

	/// <summary>是否为现代 Minecraft 皮肤(64x64)。</summary>
	public static bool IsMinecraftSkin(int width, int height) => width == 64 && height == 64;

	/// <summary>是否为 Luanti 皮肤(64x32,与老版 MC 布局一致)。</summary>
	public static bool IsLuantiSkin(int width, int height) => width == 64 && height == 32;

	/// <summary>MC 64x64 → Luanti 64x32:裁上半部分,并把覆盖层(外套/右袖/右裤)烙进底层。</summary>
	public static byte[] MinecraftToLuanti(Image<Rgba32> mc)
	{
		if (mc.Height == 32)
			return Save(mc); // 已是 Luanti 格式

		using var outImg = mc.Clone(ctx => ctx.Crop(new Rectangle(0, 0, 64, 32)));

		// 覆盖层合并到底层(SrcOver,alpha 混合);
		// 左侧覆盖层在 64x32 中无对应区域(Luanti 模型自动镜像右臂/右腿),忽略。
		Overlay(outImg, mc, JacketMc, BodyBase.Location);
		Overlay(outImg, mc, RightSleeveMc, RightArmBase.Location);
		Overlay(outImg, mc, RightPantsMc, RightLegBase.Location);

		return Save(outImg);
	}

	/// <summary>Luanti 64x32 → MC 64x64:放入上半部分,右臂/右腿复制到左臂/左腿,覆盖层留空。</summary>
	public static byte[] LuantiToMinecraft(Image<Rgba32> mt)
	{
		if (mt.Height == 64)
			return Save(mt); // 已是 MC 格式

		using var outImg = new Image<Rgba32>(64, 64);
		outImg.Mutate(ctx => ctx.DrawImage(mt, new Point(0, 0), 1f));
		// MC 渲染左肢复用相同贴图(不镜像),直接复制即可
		outImg.Mutate(ctx => ctx.DrawImage(mt.Clone(c => c.Crop(RightLegBase)), LeftLegMc.Location, 1f));
		outImg.Mutate(ctx => ctx.DrawImage(mt.Clone(c => c.Crop(RightArmBase)), LeftArmMc.Location, 1f));
		return Save(outImg);
	}

	private static void Overlay(Image<Rgba32> dest, Image<Rgba32> mc, Rectangle mcRegion, Point destLocation)
	{
		using var part = mc.Clone(ctx => ctx.Crop(mcRegion));
		dest.Mutate(ctx => ctx.DrawImage(part, destLocation, 1f));
	}

	private static byte[] Save(Image<Rgba32> img)
	{
		using var ms = new MemoryStream();
		img.SaveAsPng(ms);
		return ms.ToArray();
	}
}
