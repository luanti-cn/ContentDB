"use client";

import * as React from "react";
import Link from "next/link";
import { api, type PartyState } from "@/lib/api";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { useToast } from "@/components/ui/toast";
import {
  Check,
  Copy,
  Crown,
  LogOut,
  Loader2,
  Plus,
  UserRound,
  Users,
  X,
} from "lucide-react";

export default function PartyPage() {
  return (
    <div className="mx-auto max-w-3xl space-y-6">
      <div className="flex items-center gap-2">
        <Users className="size-5 text-primary" />
        <h1 className="text-2xl font-bold tracking-tight">组队联机</h1>
      </div>
      <p className="text-sm text-muted-foreground">
        建房把邀请码分享给好友,队长设好目标服务器,成员客户端会实时看到要去的服。
      </p>
      <PartyView />
    </div>
  );
}

function PartyView() {
  const { toast } = useToast();
  const [party, setParty] = React.useState<PartyState | null>(null);
  const [me, setMe] = React.useState<string | null>(null);
  const [loaded, setLoaded] = React.useState(false);
  const [error, setError] = React.useState<string | null>(null);
  const [serverAddr, setServerAddr] = React.useState("");
  const [joinCode, setJoinCode] = React.useState("");
  const [busy, setBusy] = React.useState(false);

  const load = React.useCallback(() => {
    api
      .partyState()
      .then((r) => {
        setParty(r.party);
        setLoaded(true);
        setError(null);
      })
      .catch((e) => {
        setLoaded(true);
        setError(e instanceof Error ? e.message : "加载失败");
      });
  }, []);

  React.useEffect(() => {
    api.whoami().then((r) => setMe(r.username)).catch(() => setMe(null));
    load();
    // 在房间时 10 秒轮询成员状态
    const timer = setInterval(() => {
      api.partyState().then((r) => setParty(r.party)).catch(() => {});
    }, 10000);
    return () => clearInterval(timer);
  }, [load]);

  React.useEffect(() => {
    setServerAddr(party?.serverAddress ?? "");
  }, [party?.id, party?.serverAddress]);

  async function act(fn: () => Promise<unknown>, ok: string) {
    setBusy(true);
    try {
      await fn();
      toast(ok);
      load();
    } catch (err) {
      toast(err instanceof Error ? err.message : "操作失败", "error");
    } finally {
      setBusy(false);
    }
  }

  if (!loaded) return <div className="h-40 animate-pulse rounded-md bg-muted" />;

  if (error) {
    return (
      <div className="rounded-lg border border-destructive/40 bg-destructive/5 p-4 text-sm text-destructive">
        {error}(可能未登录,请先{" "}
        <a href="/login" className="underline">
          登录
        </a>
        )
      </div>
    );
  }

  if (!party) {
    return (
      <Card>
        <CardHeader>
          <CardTitle className="text-base">开始组队</CardTitle>
        </CardHeader>
        <CardContent className="space-y-4">
          <form
            onSubmit={(e) => {
              e.preventDefault();
              act(() => api.createParty(), "房间已创建");
            }}
            className="flex items-center gap-2"
          >
            <Button type="submit" disabled={busy}>
              {busy ? <Loader2 className="animate-spin" /> : <Plus />} 创建房间
            </Button>
            <div className="flex flex-1 items-center gap-2">
              <Input
                value={joinCode}
                onChange={(e) => setJoinCode(e.target.value.toUpperCase())}
                placeholder="输入好友的邀请码"
                className="max-w-48"
              />
              <Button
                type="button"
                variant="outline"
                disabled={busy || !joinCode.trim()}
                onClick={() => act(() => api.joinParty(joinCode.trim()), "已加入房间")}
              >
                加入
              </Button>
            </div>
          </form>
        </CardContent>
      </Card>
    );
  }

  const isLeader = party.leaderUsername === me;

  return (
    <div className="space-y-4">
      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-3 text-base">
            邀请码
            <span className="rounded-md border bg-muted px-3 py-1 font-mono text-xl font-bold tracking-[0.3em]">
              {party.code}
            </span>
            <CopyCodeButton code={party.code} />
          </CardTitle>
        </CardHeader>
        <CardContent className="space-y-3">
          <div className="flex flex-wrap items-center gap-2">
            <Input
              value={serverAddr}
              onChange={(e) => setServerAddr(e.target.value)}
              placeholder="目标服务器 play.example.com:30000"
              className="max-w-72"
              disabled={!isLeader}
            />
            <Button
              size="sm"
              disabled={!isLeader || busy || !serverAddr.trim()}
              onClick={() => act(() => api.setPartyServer(serverAddr.trim()), "目标服务器已更新")}
            >
              {isLeader ? "设置目标服" : "仅队长可设置"}
            </Button>
            <div className="flex-1" />
            <Button
              size="sm"
              variant="ghost"
              disabled={busy}
              onClick={() =>
                act(
                  () => (isLeader ? api.endParty() : api.leaveParty()),
                  isLeader ? "房间已解散" : "已退出房间"
                )
              }
            >
              <LogOut /> {isLeader ? "解散房间" : "退出房间"}
            </Button>
          </div>
          {party.serverAddress && (
            <p className="text-sm text-muted-foreground">
              目标服务器:<code className="font-medium text-foreground">{party.serverAddress}</code>
              (客户端轮询房间状态即可一键跟随)
            </p>
          )}
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle className="text-base">成员({party.members.length})</CardTitle>
        </CardHeader>
        <CardContent className="space-y-2">
          {party.members.map((m) => (
            <div key={m.username} className="flex items-center gap-3 rounded-md border px-3 py-2">
              <div className="flex size-8 shrink-0 items-center justify-center rounded-full border bg-muted">
                <UserRound className="size-4 text-muted-foreground" />
              </div>
              <div className="min-w-0 flex-1">
                <div className="flex items-center gap-2">
                  <Link href={`/users/${m.username}`} className="truncate text-sm font-medium hover:underline">
                    {m.displayName || m.username}
                  </Link>
                  {m.isLeader && (
                    <Badge className="bg-primary/15 text-primary">
                      <Crown className="mr-1 size-3" /> 队长
                    </Badge>
                  )}
                </div>
                <p className="truncate text-xs text-muted-foreground">
                  {m.online
                    ? m.currentServerAddress
                      ? `正在 ${m.currentServerAddress}`
                      : "在线"
                    : "离线"}
                </p>
              </div>
              {isLeader && !m.isLeader && (
                <Button
                  size="sm"
                  variant="ghost"
                  title="移出房间"
                  onClick={() => {
                    if (confirm(`将 ${m.username} 移出房间?`))
                      act(() => api.kickPartyMember(m.username), "已移出");
                  }}
                >
                  <X />
                </Button>
              )}
            </div>
          ))}
        </CardContent>
      </Card>
    </div>
  );
}

function CopyCodeButton({ code }: { code: string }) {
  const [copied, setCopied] = React.useState(false);
  async function copy() {
    try {
      await navigator.clipboard.writeText(code);
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    } catch {
      /* ignore */
    }
  }
  return (
    <Button size="sm" variant="outline" onClick={copy}>
      {copied ? <Check /> : <Copy />} {copied ? "已复制" : "复制"}
    </Button>
  );
}
