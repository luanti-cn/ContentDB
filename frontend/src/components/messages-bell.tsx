"use client";

// 头部消息入口:未读总数 + WS 实时增减

import * as React from "react";
import Link from "next/link";
import { MessageCircle } from "lucide-react";
import { api } from "@/lib/api";
import { useRealtimeEvent } from "@/lib/realtime";
import { Button } from "@/components/ui/button";

export function MessagesBell() {
  const [total, setTotal] = React.useState<number | null>(null);

  const load = React.useCallback(() => {
    api
      .unreadMessages()
      .then((r) => setTotal(r.total))
      .catch(() => setTotal(null)); // 未登录
  }, []);

  React.useEffect(() => {
    load();
  }, [load]);

  useRealtimeEvent((ev) => {
    if (ev.type === "dm.new") setTotal((t) => (t ?? 0) + 1);
    else if (ev.type === "dm.read") load();
  });

  return (
    <Link href="/messages" className="relative inline-flex">
      <Button variant="ghost" size="icon" title="消息">
        <MessageCircle />
      </Button>
      {total !== null && total > 0 && (
        <span className="absolute -right-0.5 -top-0.5 flex h-4 min-w-4 items-center justify-center rounded-full bg-destructive px-1 text-[10px] font-semibold text-destructive-foreground">
          {total > 99 ? "99+" : total}
        </span>
      )}
    </Link>
  );
}
