import Link from "next/link";
import { ChevronLeft, ChevronRight } from "lucide-react";
import { cn } from "@/lib/utils";
import { buttonVariants } from "@/components/ui/button";

interface PaginationProps {
  current: number;
  pageCount: number;
  basePath: string;
  baseParams?: Record<string, string>;
}

function hrefFor(basePath: string, params: Record<string, string>, page: number): string {
  const sp = new URLSearchParams(params);
  if (page > 1) sp.set("page", String(page));
  const qs = sp.toString();
  return qs ? `${basePath}?${qs}` : basePath;
}

/** 生成页码序列(带省略号):1 … c-1 c c+1 … n */
function pageItems(current: number, pageCount: number): (number | "…")[] {
  const items: (number | "…")[] = [];
  const push = (v: number | "…") => items.push(v);

  const window = 1; // 当前页左右各显示 1 个
  const first = 1;
  const last = pageCount;

  push(first);
  const from = Math.max(first + 1, current - window);
  const to = Math.min(last - 1, current + window);

  if (from > first + 1) push("…");
  for (let i = from; i <= to; i++) push(i);
  if (to < last - 1) push("…");

  if (last > first) push(last);
  return items;
}

export function Pagination({ current, pageCount, basePath, baseParams = {} }: PaginationProps) {
  if (pageCount <= 1) return null;

  const items = pageItems(current, pageCount);
  const prevDisabled = current <= 1;
  const nextDisabled = current >= pageCount;

  return (
    <nav className="mt-8 flex items-center justify-center gap-1" aria-label="分页">
      <PageLink
        href={hrefFor(basePath, baseParams, current - 1)}
        disabled={prevDisabled}
        aria-label="上一页"
      >
        <ChevronLeft className="size-4" />
      </PageLink>

      {items.map((it, idx) =>
        it === "…" ? (
          <span
            key={`ellipsis-${idx}`}
            className="px-2 text-sm text-muted-foreground select-none"
          >
            …
          </span>
        ) : (
          <PageLink
            key={it}
            href={hrefFor(basePath, baseParams, it)}
            active={it === current}
          >
            {it}
          </PageLink>
        )
      )}

      <PageLink
        href={hrefFor(basePath, baseParams, current + 1)}
        disabled={nextDisabled}
        aria-label="下一页"
      >
        <ChevronRight className="size-4" />
      </PageLink>
    </nav>
  );
}

function PageLink({
  href,
  active,
  disabled,
  children,
  ...props
}: {
  href: string;
  active?: boolean;
  disabled?: boolean;
  children: React.ReactNode;
} & React.AnchorHTMLAttributes<HTMLAnchorElement>) {
  const className = cn(
    buttonVariants({ variant: active ? "default" : "outline", size: "icon" }),
    "size-9",
    disabled && "pointer-events-none opacity-50"
  );

  if (disabled) {
    return (
      <span className={className} aria-disabled {...props}>
        {children}
      </span>
    );
  }

  return (
    <Link href={href} className={className} {...props}>
      {children}
    </Link>
  );
}
