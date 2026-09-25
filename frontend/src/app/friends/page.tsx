"use client";

import * as React from "react";
import Link from "next/link";
import { api, type FriendInfo, type FriendRequestInfo } from "@/lib/api";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import {
  DropdownMenu,
  DropdownMenuItem,
} from "@/components/ui/dropdown-menu";
import { useToast } from "@/components/ui/toast";
import {
  Ban,
  Check,
  Loader2,
  MoreHorizontal,
  Trash2,
  UserPlus,
  UserRound,
  Users,
  X,
} from "lucide-react";

/** 在线状态副标题:在线显示所在服务器,离线显示上次在线时间。 */
function presenceSubtitle(f: FriendInfo): string {
  if (f.online) {
    return f.currentServerAddress ? `正在 ${f.currentServerAddress}` : "在线";
  }
  if (f.presenceAt) {
    const t = new Date(f.presenceAt);
    const sameDay = new Date().toDateString() === t.toDateString();
    return `离线 · 上次在线 ${
      sameDay
        ? t.toLocaleTimeString("zh-CN", { hour: "2-digit", minute: "2-digit" })
        : t.toLocaleString("zh-CN", {
            month: "2-digit",
            day: "2-digit",
            hour: "2-digit",
            minute: "2-digit",
          })
    }`;
  }
  return "离线";
}

export default function FriendsPage() {
  const { toast } = useToast();
  const [friends, setFriends] = React.useState<FriendInfo[] | null>(null);
  const [incoming, setIncoming] = React.useState<FriendRequestInfo[]>([]);
  const [outgoing, setOutgoing] = React.useState<FriendRequestInfo[]>([]);
  const [error, setError] = React.useState<string | null>(null);
  const [username, setUsername] = React.useState("");
  const [busy, setBusy] = React.useState(false);

  const load = React.useCallback(() => {
    api
      .listFriends()
      .then((r) => {
        setFriends(r.friends);
        setError(null);
      })
      .catch((e) => setError(e instanceof Error ? e.message : "加载失败"));
    api
      .listFriendRequests()
      .then((r) => {
        setIncoming(r.incoming);
        setOutgoing(r.outgoing);
      })
      .catch(() => {
        /* 未登录时忽略 */
      });
  }, []);

  React.useEffect(() => {
    load();
    // 在线状态 30 秒自动刷新
    const timer = setInterval(() => {
      api.listFriends().then((r) => setFriends(r.friends)).catch(() => {});
    }, 30000);
    return () => clearInterval(timer);
  }, [load]);

  async function send(e: React.FormEvent) {
    e.preventDefault();
    if (!username.trim()) {
      toast("请填写用户名", "error");
      return;
    }
    setBusy(true);
    try {
      const res = await api.sendFriendRequest(username.trim());
      toast(res.accepted ? "对方早已申请,你们已成为好友" : "申请已发送");
      setUsername("");
      load();
    } catch (err) {
      toast(err instanceof Error ? err.message : "发送失败", "error");
    } finally {
      setBusy(false);
    }
  }

  async function act(fn: () => Promise<unknown>, ok: string) {
    try {
      await fn();
      toast(ok);
      load();
    } catch (err) {
      toast(err instanceof Error ? err.message : "操作失败", "error");
    }
  }

  return (
    <div className="mx-auto max-w-3xl space-y-6">
      <div className="flex items-center gap-2">
        <Users className="size-5 text-primary" />
        <h1 className="text-2xl font-bold tracking-tight">好友</h1>
      </div>

      {error && (
        <div className="rounded-lg border border-destructive/40 bg-destructive/5 p-4 text-sm text-destructive">
          {error}(可能未登录,请先{" "}
          <a href="/login" className="underline">
            登录
          </a>
          )
        </div>
      )}

      <Card>
        <CardHeader>
          <CardTitle className="text-base">添加好友</CardTitle>
        </CardHeader>
        <CardContent>
          <form onSubmit={send} className="flex items-end gap-3">
            <div className="flex-1 space-y-1.5">
              <Label>用户名</Label>
              <Input
                value={username}
                onChange={(e) => setUsername(e.target.value)}
                placeholder="站内用户名"
              />
            </div>
            <Button type="submit" disabled={busy}>
              {busy ? <Loader2 className="animate-spin" /> : <UserPlus />} 发送申请
            </Button>
          </form>
        </CardContent>
      </Card>

      {(incoming.length > 0 || outgoing.length > 0) && (
        <Card>
          <CardHeader>
            <CardTitle className="text-base">
              好友申请
              {incoming.length > 0 && (
                <Badge className="ml-2 bg-primary/15 text-primary">{incoming.length} 条待处理</Badge>
              )}
            </CardTitle>
          </CardHeader>
          <CardContent className="space-y-4">
            {incoming.length > 0 && (
              <div>
                <p className="mb-2 text-xs font-medium text-muted-foreground">收到的</p>
                <div className="space-y-2">
                  {incoming.map((r) => (
                    <div
                      key={r.id}
                      className="flex items-center justify-between rounded-md border px-3 py-2"
                    >
                      <Link href={`/users/${r.username}`} className="text-sm font-medium hover:underline">
                        {r.displayName || r.username}
                        {r.displayName && (
                          <span className="ml-1.5 text-xs text-muted-foreground">@{r.username}</span>
                        )}
                      </Link>
                      <div className="flex gap-1">
                        <Button
                          size="sm"
                          onClick={() => act(() => api.acceptFriendRequest(r.id), "已接受")}
                        >
                          <Check /> 接受
                        </Button>
                        <Button
                          size="sm"
                          variant="ghost"
                          onClick={() => act(() => api.rejectFriendRequest(r.id), "已拒绝")}
                        >
                          <X /> 拒绝
                        </Button>
                      </div>
                    </div>
                  ))}
                </div>
              </div>
            )}
            {outgoing.length > 0 && (
              <div>
                <p className="mb-2 text-xs font-medium text-muted-foreground">发出的</p>
                <div className="space-y-2">
                  {outgoing.map((r) => (
                    <div
                      key={r.id}
                      className="flex items-center justify-between rounded-md border px-3 py-2"
                    >
                      <Link href={`/users/${r.username}`} className="text-sm font-medium hover:underline">
                        {r.displayName || r.username}
                        {r.displayName && (
                          <span className="ml-1.5 text-xs text-muted-foreground">@{r.username}</span>
                        )}
                      </Link>
                      <Button
                        size="sm"
                        variant="ghost"
                        onClick={() => act(() => api.rejectFriendRequest(r.id), "已撤回")}
                      >
                        <X /> 撤回
                      </Button>
                    </div>
                  ))}
                </div>
              </div>
            )}
          </CardContent>
        </Card>
      )}

      <Card>
        <CardHeader>
          <CardTitle className="text-base">我的好友</CardTitle>
        </CardHeader>
        <CardContent>
          {friends === null && !error ? (
            <div className="h-24 animate-pulse rounded-md bg-muted" />
          ) : friends && friends.length === 0 ? (
            <p className="text-sm text-muted-foreground">还没有好友,从上面搜索添加吧。</p>
          ) : friends ? (
            <div className="space-y-2">
              {friends.map((f) => (
                <div
                  key={f.username}
                  className="flex items-center gap-3 rounded-md border px-3 py-2.5"
                >
                  {f.profilePicUrl ? (
                    // eslint-disable-next-line @next/next/no-img-element
                    <img
                      src={f.profilePicUrl}
                      alt={f.username}
                      className="size-9 shrink-0 rounded-full border object-cover"
                    />
                  ) : (
                    <div className="flex size-9 shrink-0 items-center justify-center rounded-full border bg-muted">
                      <UserRound className="size-4 text-muted-foreground" />
                    </div>
                  )}
                  <div className="min-w-0 flex-1">
                    <div className="flex items-center gap-2">
                      <Link
                        href={`/users/${f.username}`}
                        className="truncate text-sm font-medium hover:underline"
                      >
                        {f.displayName || f.username}
                      </Link>
                    </div>
                    <p className="truncate text-xs text-muted-foreground">
                      {presenceSubtitle(f)}
                    </p>
                  </div>
                  <span
                    className={`flex shrink-0 items-center gap-1.5 rounded-full px-2 py-0.5 text-xs font-medium ${
                      f.online
                        ? "bg-emerald-500/10 text-emerald-600"
                        : "bg-muted text-muted-foreground"
                    }`}
                  >
                    <span
                      className={`size-1.5 rounded-full ${
                        f.online ? "bg-emerald-500" : "bg-muted-foreground/50"
                      }`}
                    />
                    {f.online ? "在线" : "离线"}
                  </span>
                  <DropdownMenu
                    trigger={
                      <Button size="sm" variant="ghost" title="更多操作">
                        <MoreHorizontal />
                      </Button>
                    }
                  >
                    <DropdownMenuItem
                      onClick={() => {
                        if (confirm(`删除好友 ${f.username}?`))
                          act(() => api.removeFriend(f.username), "已删除");
                      }}
                    >
                      <Trash2 className="size-4" /> 删除好友
                    </DropdownMenuItem>
                    <DropdownMenuItem
                      destructive
                      onClick={() => {
                        if (confirm(`拉黑 ${f.username}?将自动删除好友关系。`))
                          act(() => api.blockUser(f.username), "已拉黑");
                      }}
                    >
                      <Ban className="size-4" /> 拉黑
                    </DropdownMenuItem>
                  </DropdownMenu>
                </div>
              ))}
            </div>
          ) : null}
        </CardContent>
      </Card>
    </div>
  );
}
