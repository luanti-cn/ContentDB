import Link from "next/link";
import { cn } from "@/lib/utils";
import { buttonVariants } from "@/components/ui/button";

interface SourceFilterProps {
  sources: { id: string; name: string }[];
  current?: string;
  type?: string;
  q?: string;
}

function href(params: Record<string, string | undefined>): string {
  const sp = new URLSearchParams();
  for (const [k, v] of Object.entries(params)) if (v) sp.set(k, v);
  const s = sp.toString();
  return s ? `/?${s}` : "/";
}

/** 来源筛选条:全部 + 各来源站点。切换时重置分页。 */
export function SourceFilter({ sources, current, type, q }: SourceFilterProps) {
  const chip = (active: boolean) =>
    cn(
      buttonVariants({ variant: active ? "default" : "outline", size: "sm" }),
      "h-8"
    );

  return (
    <div className="flex flex-wrap items-center gap-1.5">
      <span className="mr-1 text-sm text-muted-foreground">来源:</span>
      <Link href={href({ type, q })} className={chip(!current)}>
        全部
      </Link>
      {sources.map((s) => (
        <Link
          key={s.id}
          href={href({ type, q, source: s.id })}
          className={chip(current === s.id)}
        >
          {s.name}
        </Link>
      ))}
    </div>
  );
}
