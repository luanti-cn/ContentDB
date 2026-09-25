import Link from "next/link";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import {
  ArrowRight,
  Blocks,
  CloudUpload,
  Download,
  Globe,
  Languages,
  Server,
  Sparkles,
  Users,
} from "lucide-react";

export default function HomePage() {
  return (
    <div className="space-y-14">
      {/* Hero */}
      <section className="rounded-2xl border bg-gradient-to-br from-primary/10 via-background to-background p-10 text-center sm:p-16">
        <div className="mx-auto flex max-w-3xl flex-col items-center gap-5">
          <span className="flex size-16 items-center justify-center rounded-2xl bg-primary text-primary-foreground">
            <Blocks className="size-8" />
          </span>
          <h1 className="text-4xl font-bold tracking-tight sm:text-5xl">LuantiCN</h1>
          <p className="text-lg leading-relaxed text-muted-foreground">
            免费开源的体素游戏平台 Luanti(原 Minetest)中文本土化发行版。
            中文界面开箱即用,账号云端同步,国内服务器一键直达,海量 Mod 与子游戏极速下载。
          </p>
          <div className="flex flex-wrap items-center justify-center gap-3">
            <Button asChild size="lg" className="h-11">
              <a href="https://luanti.cn/download">
                <Download /> 下载客户端
              </a>
            </Button>
            <Button asChild size="lg" variant="outline" className="h-11">
              <Link href="/explore">
                浏览内容库 <ArrowRight />
              </Link>
            </Button>
          </div>
          <p className="text-xs text-muted-foreground">
            支持 Windows / Android / Linux / macOS · 永久免费 · LGPL 开源
          </p>
        </div>
      </section>

      {/* 特性 */}
      <section>
        <h2 className="text-center text-2xl font-bold tracking-tight">为国内玩家而生</h2>
        <div className="mt-8 grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
          {[
            {
              icon: Languages,
              title: "完整中文体验",
              desc: "简体中文界面、中文化的错误提示与新手引导,字体渲染开箱即用。",
            },
            {
              icon: CloudUpload,
              title: "账号云同步",
              desc: "配对设备后,进服务器自动取回账号密码;角色(人物卡)、好友在线状态云端管理。",
            },
            {
              icon: Server,
              title: "国内服务器列表",
              desc: "内置国内服务器列表,延迟排序、一键加入;服主提交申请即可被全网玩家看到。",
            },
            {
              icon: Globe,
              title: "内容极速下载",
              desc: "官方内容库国内镜像 + CDN 直链,Mod、子游戏、材质包下载不再龟速。",
            },
          ].map((f) => (
            <Card key={f.title}>
              <CardContent className="space-y-2 pt-6">
                <span className="flex size-10 items-center justify-center rounded-lg bg-primary/10 text-primary">
                  <f.icon className="size-5" />
                </span>
                <h3 className="font-semibold">{f.title}</h3>
                <p className="text-sm leading-relaxed text-muted-foreground">{f.desc}</p>
              </CardContent>
            </Card>
          ))}
        </div>
      </section>

      {/* 下载 */}
      <section id="download" className="rounded-2xl border p-8 sm:p-10">
        <div className="flex items-center gap-2">
          <Download className="size-5 text-primary" />
          <h2 className="text-2xl font-bold tracking-tight">下载 LuantiCN</h2>
        </div>
        <div className="mt-6 grid gap-4 sm:grid-cols-3">
          {[
            { name: "Windows", desc: "Windows 10/11 x64 安装即用", href: "https://luanti.cn/download#windows" },
            { name: "Android", desc: "Android 5.0+,支持各主流架构", href: "https://luanti.cn/download#android" },
            { name: "Linux / macOS", desc: "AppImage / dmg,亦可用包管理器安装", href: "https://luanti.cn/download#desktop" },
          ].map((d) => (
            <a
              key={d.name}
              href={d.href}
              className="group rounded-xl border p-5 transition-colors hover:border-primary/50 hover:bg-accent"
            >
              <div className="flex items-center justify-between">
                <span className="font-semibold">{d.name}</span>
                <ArrowRight className="size-4 text-muted-foreground transition-transform group-hover:translate-x-0.5" />
              </div>
              <p className="mt-1 text-sm text-muted-foreground">{d.desc}</p>
            </a>
          ))}
        </div>
      </section>

      {/* 入口 */}
      <section className="grid gap-4 sm:grid-cols-3">
        {[
          { href: "/explore", icon: Blocks, title: "内容库", desc: "Mod · 子游戏 · 材质包" },
          { href: "/servers", icon: Server, title: "服务器大厅", desc: "收录你的服务器,展示实时状态" },
          { href: "/cloud", icon: Users, title: "云同步", desc: "设备配对 · 角色 · 好友 · 组队" },
        ].map((c) => (
          <Link
            key={c.href}
            href={c.href}
            className="group rounded-xl border p-5 transition-colors hover:border-primary/50 hover:bg-accent"
          >
            <div className="flex items-center gap-2 font-semibold">
              <c.icon className="size-4 text-primary" />
              {c.title}
            </div>
            <p className="mt-1 text-sm text-muted-foreground">{c.desc}</p>
          </Link>
        ))}
      </section>

      {/* 社区 */}
      <section className="rounded-2xl border bg-muted/30 p-8 text-center">
        <div className="mx-auto flex max-w-2xl flex-col items-center gap-3">
          <Sparkles className="size-5 text-primary" />
          <h2 className="text-xl font-bold tracking-tight">加入社区</h2>
          <p className="text-sm text-muted-foreground">
            LuantiCN 基于 Luanti 引擎(原 Minetest),由社区驱动开发。
            无论是玩家、服主还是 Mod 作者,都欢迎参与共建。
          </p>
          <div className="mt-2 flex flex-wrap justify-center gap-3">
            <Button asChild variant="outline" size="sm">
              <a href="https://luanti.cn/forum" target="_blank" rel="noreferrer">
                <Globe /> 中文论坛
              </a>
            </Button>
            <Button asChild variant="outline" size="sm">
              <a href="https://luanti.cn/wiki" target="_blank" rel="noreferrer">
                <Blocks /> 中文 Wiki
              </a>
            </Button>
            <Button asChild variant="outline" size="sm">
              <a href="https://github.com/luanti-org/luanti" target="_blank" rel="noreferrer">
                上游引擎
              </a>
            </Button>
          </div>
        </div>
      </section>
    </div>
  );
}
