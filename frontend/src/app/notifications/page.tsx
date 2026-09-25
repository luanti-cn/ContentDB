"use client";

import * as React from "react";
import { api, type NotificationItem } from "@/lib/api";
import { Card, CardContent } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { Bell, BellOff } from "lucide-react";

export default function NotificationsPage() {
  const [items, setItems] = React.useState<NotificationItem[] | null>(null);
  const [error, setError] = React.useState<string | null>(null);

  React.useEffect(() => {
    api
      .notifications()
      .then(setItems)
      .catch((e) => setError(e instanceof Error ? e.message : "加载失败"));
  }, []);

  return (
    <div className="mx-auto max-w-2xl space-y-6">
      <div className="flex items-center gap-2">
        <Bell className="size-5 text-primary" />
        <h1 className="text-2xl font-bold tracking-tight">通知</h1>
      </div>

      {error ? (
        <div className="rounded-lg border border-destructive/40 bg-destructive/5 p-4 text-sm text-destructive">
          {error}(可能未登录)
        </div>
      ) : items === null ? (
        <div className="space-y-3">
          {[0, 1, 2].map((i) => (
            <div key={i} className="h-16 animate-pulse rounded-lg bg-muted" />
          ))}
        </div>
      ) : items.length === 0 ? (
        <div className="flex flex-col items-center gap-3 rounded-lg border border-dashed py-16 text-muted-foreground">
          <BellOff className="size-8 opacity-40" />
          <p>暂无通知。</p>
        </div>
      ) : (
        <div className="space-y-3">
          {items.map((n) => (
            <Card key={n.id} className={n.read ? "opacity-70" : ""}>
              <CardContent className="flex items-start justify-between gap-4 py-4">
                <div className="space-y-1">
                  <a href={n.url} className="font-medium hover:underline">
                    {n.title}
                  </a>
                  <p className="text-xs text-muted-foreground">
                    {new Date(n.createdAt).toLocaleString("zh-CN")}
                    {n.packageKey ? ` · ${n.packageKey}` : ""}
                  </p>
                </div>
                {!n.read && <Badge>未读</Badge>}
              </CardContent>
            </Card>
          ))}
        </div>
      )}
    </div>
  );
}
