"use client";

import * as React from "react";
import { api, type GameServerInfo, type MyGameServerInfo } from "@/lib/api";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { useToast } from "@/components/ui/toast";
import {
  Check,
  Copy,
  Globe,
  KeyRound,
  Loader2,
  Plus,
  RefreshCw,
  Server,
  Trash2,
} from "lucide-react";

export default function ServersPage() {
  return (
    <div className="mx-auto max-w-3xl space-y-6">
      <div className="flex items-center gap-2">
        <Server className="size-5 text-primary" />
        <h1 className="text-2xl font-bold tracking-tight">服务器大厅</h1>
      </div>

      <RegisterSection />
      <PublicServers />
      <MyServers />
    </div>
  );
}

function RegisterSection() {
  const { toast } = useToast();
  const [address, setAddress] = React.useState("");
  const [name, setName] = React.useState("");
  const [description, setDescription] = React.useState("");
  const [busy, setBusy] = React.useState(false);
  const [token, setToken] = React.useState<{ address: string; token: string } | null>(null);

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    if (!address.trim() || !name.trim()) {
      toast("请填写服务器地址和名称", "error");
      return;
    }
    setBusy(true);
    try {
      const res = await api.registerServer({ address: address.trim(), name: name.trim(), description });
      setToken({ address: res.address, token: res.reportToken });
      setAddress("");
      setName("");
      setDescription("");
      toast("服务器已收录");
    } catch (err) {
      toast(err instanceof Error ? err.message : "收录失败", "error");
    } finally {
      setBusy(false);
    }
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">收录服务器</CardTitle>
      </CardHeader>
      <CardContent className="space-y-4">
        <form onSubmit={submit} className="space-y-3">
          <div className="grid gap-3 sm:grid-cols-2">
            <div className="space-y-1.5">
              <Label>服务器地址</Label>
              <Input
                value={address}
                onChange={(e) => setAddress(e.target.value)}
                placeholder="play.example.com:30000"
              />
            </div>
            <div className="space-y-1.5">
              <Label>名称</Label>
              <Input value={name} onChange={(e) => setName(e.target.value)} placeholder="我的服务器" />
            </div>
          </div>
          <div className="space-y-1.5">
            <Label>简介(可选)</Label>
            <Textarea
              rows={2}
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              placeholder="生存 / 创造 / 玩法介绍…"
            />
          </div>
          <Button type="submit" disabled={busy}>
            {busy ? <Loader2 className="animate-spin" /> : <Plus />} 收录
          </Button>
        </form>

        {token && <TokenReveal address={token.address} token={token.token} />}
      </CardContent>
    </Card>
  );
}

function TokenReveal({ address, token }: { address: string; token: string }) {
  const { toast } = useToast();
  const [copied, setCopied] = React.useState(false);

  async function copy() {
    try {
      await navigator.clipboard.writeText(token);
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    } catch {
      /* ignore */
    }
  }

  return (
    <div className="space-y-2 rounded-lg border border-primary/40 bg-primary/5 p-4">
      <p className="text-sm font-medium">
        「{address}」的上报 Token 已生成 — 只显示这一次,请立即复制:
      </p>
      <div className="flex items-center gap-2">
        <code className="flex-1 overflow-x-auto rounded-md border bg-background px-3 py-2 text-xs">
          {token}
        </code>
        <Button size="sm" variant="outline" onClick={copy}>
          {copied ? <Check /> : <Copy />} {copied ? "已复制" : "复制"}
        </Button>
      </div>
      <p className="text-xs text-muted-foreground">
        把它配置到服务器端上报 mod,服务会每 60 秒向
        <code className="mx-1">POST /api/servers/report/</code>推送在线人数。
      </p>
    </div>
  );
}

function PublicServers() {
  const [servers, setServers] = React.useState<GameServerInfo[] | null>(null);

  React.useEffect(() => {
    const load = () => api.listServers().then((r) => setServers(r.servers)).catch(() => setServers([]));
    load();
    const timer = setInterval(load, 30000);
    return () => clearInterval(timer);
  }, []);

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">全部服务器</CardTitle>
      </CardHeader>
      <CardContent className="space-y-2">
        {servers === null ? (
          <div className="h-16 animate-pulse rounded-md bg-muted" />
        ) : servers.length === 0 ? (
          <p className="text-sm text-muted-foreground">还没有收录的服务器。</p>
        ) : (
          servers.map((s) => (
            <div key={s.id} className="flex items-center gap-3 rounded-md border px-3 py-2.5">
              <span
                className={`size-2 shrink-0 rounded-full ${s.online ? "bg-emerald-500" : "bg-muted-foreground/40"}`}
                title={s.online ? "在线" : "离线"}
              />
              <div className="min-w-0 flex-1">
                <div className="flex items-center gap-2">
                  <span className="truncate text-sm font-medium">{s.name}</span>
                  {s.verified && <Badge className="bg-primary/15 text-primary">认证</Badge>}
                </div>
                <p className="truncate text-xs text-muted-foreground">
                  <code>{s.address}</code>
                  {s.description ? ` · ${s.description}` : ""}
                </p>
              </div>
              <div className="shrink-0 text-right text-xs text-muted-foreground">
                <p>
                  {s.online ? `${s.playersOnline}/${s.playersMax} 人` : "离线"}
                  {s.ourPlayersOnline > 0 && ` · 本站 ${s.ourPlayersOnline} 人`}
                </p>
                <p>@{s.owner}</p>
              </div>
            </div>
          ))
        )}
      </CardContent>
    </Card>
  );
}

function MyServers() {
  const { toast } = useToast();
  const [servers, setServers] = React.useState<MyGameServerInfo[] | null>(null);
  const [error, setError] = React.useState<string | null>(null);

  const load = React.useCallback(() => {
    api
      .listMyServers()
      .then((r) => {
        setServers(r.servers);
        setError(null);
      })
      .catch((e) => setError(e instanceof Error ? e.message : "加载失败"));
  }, []);

  React.useEffect(() => {
    load();
  }, [load]);

  async function regen(id: number) {
    if (!confirm("重新生成上报 Token?旧 Token 立即失效。")) return;
    try {
      const res = await api.regenerateServerToken(id);
      toast("新 Token 已生成,请立即复制");
      // 复用 TokenReveal 展示:简单起见用弹层提示
      try {
        await navigator.clipboard.writeText(res.reportToken);
        toast("新 Token 已复制到剪贴板");
      } catch {
        /* ignore */
      }
      load();
    } catch (err) {
      toast(err instanceof Error ? err.message : "操作失败", "error");
    }
  }

  async function toggleListed(s: MyGameServerInfo) {
    try {
      await api.updateServer(s.id, { listed: !s.listed });
      load();
    } catch (err) {
      toast(err instanceof Error ? err.message : "操作失败", "error");
    }
  }

  async function remove(id: number) {
    if (!confirm("删除该收录?")) return;
    try {
      await api.deleteServer(id);
      toast("已删除");
      load();
    } catch (err) {
      toast(err instanceof Error ? err.message : "删除失败", "error");
    }
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">我收录的服务器</CardTitle>
      </CardHeader>
      <CardContent className="space-y-2">
        {error && <p className="text-sm text-destructive">{error}</p>}
        {servers === null && !error ? (
          <div className="h-16 animate-pulse rounded-md bg-muted" />
        ) : servers && servers.length === 0 ? (
          <p className="text-sm text-muted-foreground">还没有收录。在上方提交地址即可获得上报 Token。</p>
        ) : (
          servers?.map((s) => (
            <div key={s.id} className="flex items-center gap-3 rounded-md border px-3 py-2.5">
              <Globe className="size-4 shrink-0 text-muted-foreground" />
              <div className="min-w-0 flex-1">
                <div className="flex items-center gap-2">
                  <span className="truncate text-sm font-medium">{s.name}</span>
                  {!s.listed && <Badge variant="secondary">未上架</Badge>}
                </div>
                <p className="truncate text-xs text-muted-foreground">
                  <code>{s.address}</code> ·{" "}
                  {s.online ? `${s.playersOnline}/${s.playersMax} 人` : "无上报(离线)"}
                </p>
              </div>
              <div className="flex shrink-0 gap-1">
                <Button size="sm" variant="ghost" title="切换上架" onClick={() => toggleListed(s)}>
                  {s.listed ? "下架" : "上架"}
                </Button>
                <Button size="sm" variant="ghost" title="重新生成 Token" onClick={() => regen(s.id)}>
                  <RefreshCw />
                </Button>
                <Button size="sm" variant="ghost" title="删除" onClick={() => remove(s.id)}>
                  <Trash2 />
                </Button>
              </div>
            </div>
          ))
        )}
        {servers && servers.length > 0 && (
          <p className="flex items-center gap-1.5 text-xs text-muted-foreground">
            <KeyRound className="size-3" /> 服务器端 mod 每 60 秒 POST /api/servers/report/ 上报即可显示在线状态。
          </p>
        )}
      </CardContent>
    </Card>
  );
}
