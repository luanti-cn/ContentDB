"use client";

import * as React from "react";
import { ExternalLink, Info, X } from "lucide-react";
import { Button } from "@/components/ui/button";
import { api } from "@/lib/api";

const STORAGE_KEY = "cdb:home-notice:v1";

export function HomeNoticeDialog() {
  const [open, setOpen] = React.useState(false);

  React.useEffect(() => {
    if (localStorage.getItem(STORAGE_KEY)) return;
    let alive = true;
    api
      .whoami()
      .then((r) => {
        if (alive && !r.is_authenticated) setOpen(true);
      })
      .catch(() => {});
    return () => {
      alive = false;
    };
  }, []);

  React.useEffect(() => {
    if (!open) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") close();
    };
    document.addEventListener("keydown", onKey);
    document.body.style.overflow = "hidden";
    return () => {
      document.removeEventListener("keydown", onKey);
      document.body.style.overflow = "";
    };
  }, [open]);

  function close() {
    localStorage.setItem(STORAGE_KEY, "1");
    setOpen(false);
  }

  if (!open) return null;

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4 backdrop-blur-sm"
      onClick={close}
    >
      <div
        role="dialog"
        aria-modal="true"
        aria-labelledby="home-notice-title"
        className="relative w-full max-w-md rounded-2xl border bg-card p-6 shadow-lg"
        onClick={(e) => e.stopPropagation()}
      >
        <button
          type="button"
          aria-label="关闭"
          onClick={close}
          className="absolute right-4 top-4 rounded-md p-1 text-muted-foreground transition-colors hover:bg-accent hover:text-foreground"
        >
          <X className="size-4" />
        </button>

        <div className="flex items-center gap-3">
          <span className="flex size-10 shrink-0 items-center justify-center rounded-full bg-primary/10 text-primary">
            <Info className="size-5" />
          </span>
          <h2 id="home-notice-title" className="pr-6 text-lg font-bold leading-snug">
            关于 LuantiCN 与 Luanti 官方的重要说明
          </h2>
        </div>

        <ul className="mt-5 space-y-3 text-sm leading-relaxed text-muted-foreground">
          <li className="flex gap-2.5">
            <span className="mt-2 size-1.5 shrink-0 rounded-full bg-primary/60" />
            <span>
              Luanti 官方明确表示：不希望出现仅附加中文翻译的打包版本，也不认可任何第三方镜像站点。
            </span>
          </li>
          <li className="flex gap-2.5">
            <span className="mt-2 size-1.5 shrink-0 rounded-full bg-primary/60" />
            <span>
              因此本站不再只是「汉化打包」，而是对客户端做了自有增强：云端密码同步、皮肤增强等功能。
            </span>
          </li>
          <li className="flex gap-2.5">
            <span className="mt-2 size-1.5 shrink-0 rounded-full bg-primary/60" />
            <span>应官方要求，我们在此提供通往 Luanti 官方网站的显著入口。</span>
          </li>
        </ul>

        <div className="mt-6 space-y-2">
          <Button asChild className="w-full">
            <a href="https://www.luanti.org" target="_blank" rel="noreferrer">
              访问 Luanti 官网 <ExternalLink />
            </a>
          </Button>
          <Button variant="ghost" className="w-full" onClick={close}>
            继续浏览 LuantiCN
          </Button>
        </div>
      </div>
    </div>
  );
}
