import { api, type PackageQuery, type PackageShort } from "@/lib/api";
import { PackageCard } from "@/components/package-card";
import { Input } from "@/components/ui/input";
import { Button } from "@/components/ui/button";
import { Pagination } from "@/components/pagination";
import { SourceFilter } from "@/components/source-filter";
import { Search, PackageX } from "lucide-react";

export const dynamic = "force-dynamic";

const PAGE_SIZE = 48;

const TYPE_TITLE: Record<string, string> = {
  mod: "Mod",
  game: "子游戏",
  txp: "材质包",
};

export default async function HomePage({
  searchParams,
}: {
  searchParams: Promise<Record<string, string | string[] | undefined>>;
}) {
  const sp = await searchParams;
  const type = typeof sp.type === "string" ? sp.type : undefined;
  const q = typeof sp.q === "string" ? sp.q : undefined;
  const source = typeof sp.source === "string" ? sp.source : undefined;
  const page = Math.max(1, Number.parseInt(typeof sp.page === "string" ? sp.page : "1", 10) || 1);

  // 官方 /api/packages/ 一次性返回全部结果(无服务端分页),这里取全量后在前端切分。
  const query: PackageQuery = { type, q };

  let all: PackageShort[] = [];
  let error: string | null = null;
  try {
    all = await api.listPackages(query);
  } catch (e) {
    error = e instanceof Error ? e.message : "加载失败";
  }

  // 汇总可用来源(用于筛选条)。保留合并顺序:本站在前。
  const sourceMap = new Map<string, string>();
  for (const p of all) {
    const id = p.source ?? (p.origin === "upstream" ? "upstream" : "local");
    if (!sourceMap.has(id)) sourceMap.set(id, p.source_name ?? (p.origin === "upstream" ? "上游" : "本站"));
  }
  const sources = [...sourceMap.entries()].map(([id, name]) => ({ id, name }));

  // 按来源筛选(客户端)
  const filtered = source
    ? all.filter((p) => (p.source ?? (p.origin === "upstream" ? "upstream" : "local")) === source)
    : all;

  const total = filtered.length;
  const pageCount = Math.max(1, Math.ceil(total / PAGE_SIZE));
  const current = Math.min(page, pageCount);
  const start = (current - 1) * PAGE_SIZE;
  const packages = filtered.slice(start, start + PAGE_SIZE);

  const heading = type ? TYPE_TITLE[type] ?? "内容" : "全部内容";

  const baseParams: Record<string, string> = {};
  if (type) baseParams.type = type;
  if (q) baseParams.q = q;
  if (source) baseParams.source = source;

  return (
    <div className="space-y-8">
      {/* Hero */}
      <section className="rounded-2xl border bg-gradient-to-br from-primary/10 via-background to-background p-8 sm:p-10">
        <h1 className="text-3xl font-bold tracking-tight sm:text-4xl">
          {q ? `搜索:${q}` : heading}
        </h1>
        <p className="mt-2 max-w-2xl text-muted-foreground">
          Luanti 内容库国内镜像 —— 加速浏览与下载 mod、子游戏与材质包。本站自营与官方上游内容已合并展示。
        </p>

        <form className="mt-6 flex max-w-xl gap-2" action="/explore">
          {type && <input type="hidden" name="type" value={type} />}
          {source && <input type="hidden" name="source" value={source} />}
          <div className="relative flex-1">
            <Search className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" />
            <Input
              name="q"
              defaultValue={q}
              placeholder="搜索包名、作者或关键字…"
              className="h-10 pl-9"
            />
          </div>
          <Button type="submit" size="lg" className="h-10">
            搜索
          </Button>
        </form>
      </section>

      {/* Results */}
      <section className="space-y-4">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <h2 className="text-lg font-semibold tracking-tight">
            {heading}
            {!error && (
              <span className="ml-2 text-sm font-normal text-muted-foreground">
                共 {total} 项 · 第 {current}/{pageCount} 页
              </span>
            )}
          </h2>
          {sources.length > 1 && (
            <SourceFilter sources={sources} current={source} type={type} q={q} />
          )}
        </div>

        {error ? (
          <div className="space-y-2 rounded-lg border border-destructive/40 bg-destructive/5 p-4 text-sm text-destructive">
            <p className="font-medium">加载失败:{error}</p>
            <p className="text-destructive/80">
              后端可能不可用(数据库或上游镜像连接异常)。请确认后端服务已启动,或稍后重试。
            </p>
            <a href="/explore" className="inline-block underline">
              重试
            </a>
          </div>
        ) : packages.length === 0 ? (
          <div className="flex flex-col items-center justify-center gap-3 rounded-lg border border-dashed py-16 text-center text-muted-foreground">
            <PackageX className="size-10 opacity-40" />
            {q || type || source ? (
              <p>没有找到匹配的内容。</p>
            ) : (
              <div className="space-y-1">
                <p className="font-medium text-foreground">暂无可显示的内容</p>
                <p className="max-w-md text-sm">
                  本站尚无已发布的包,且上游镜像/数据库当前不可用。
                  请检查后端数据库(Postgres)与上游站点配置,或
                  <a href="/explore" className="underline">
                    重试
                  </a>
                  。
                </p>
              </div>
            )}
          </div>
        ) : (
          <>
            <div className="grid grid-cols-1 gap-5 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">
              {packages.map((p) => (
                <PackageCard key={`${p.source ?? "local"}:${p.author}/${p.name}`} pkg={p} />
              ))}
            </div>

            <Pagination
              current={current}
              pageCount={pageCount}
              baseParams={baseParams}
              basePath="/explore"
            />
          </>
        )}
      </section>
    </div>
  );
}
