"use client";

import * as React from "react";
import { api, type AuditEntry, type AdminGameServerInfo } from "@/lib/api";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { ShieldAlert, RefreshCw, Server } from "lucide-react";

const SEVERITY_VARIANT: Record<string, "default" | "secondary" | "destructive" | "outline"> = {
  low: "outline",
  normal: "secondary",
  high: "destructive",
  critical: "destructive",
};

export default function AdminPage() {
  const [entries, setEntries] = React.useState<AuditEntry[] | null>(null);
  const [error, setError] = React.useState<string | null>(null);

  const load = React.useCallback(() => {
    setError(null);
    api
      .auditLog(200)
      .then((e) => setEntries(Array.isArray(e) ? e : []))
      .catch((err) => setError(err instanceof Error ? err.message : "加载失败"));
  }, []);

  React.useEffect(() => {
    load();
  }, [load]);

  return (
    <div className="mx-auto max-w-5xl space-y-6">
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-2">
          <ShieldAlert className="size-5 text-primary" />
          <h1 className="text-2xl font-bold tracking-tight">管理后台 · 审计日志</h1>
        </div>
        <Button variant="outline" size="sm" onClick={load}>
          <RefreshCw /> 刷新
        </Button>
      </div>

      {error ? (
        <div className="rounded-lg border border-destructive/40 bg-destructive/5 p-4 text-sm text-destructive">
          {error}(需要 EDITOR 及以上权限,请确认已登录且有权限)
        </div>
      ) : (
        <Card>
          <CardHeader>
            <CardTitle className="text-base">最近操作</CardTitle>
          </CardHeader>
          <CardContent>
            {entries === null ? (
              <div className="h-40 animate-pulse rounded-md bg-muted" />
            ) : entries.length === 0 ? (
              <p className="text-sm text-muted-foreground">暂无审计记录。</p>
            ) : (
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>时间</TableHead>
                    <TableHead>级别</TableHead>
                    <TableHead>标题</TableHead>
                    <TableHead>操作人</TableHead>
                    <TableHead>包</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {entries.map((e) => (
                    <TableRow key={e.id}>
                      <TableCell className="whitespace-nowrap text-xs text-muted-foreground">
                        {e.created_at
                          ? new Date(e.created_at).toLocaleString("zh-CN")
                          : "-"}
                      </TableCell>
                      <TableCell>
                        <Badge variant={SEVERITY_VARIANT[e.severity?.toLowerCase()] ?? "outline"}>
                          {e.severity}
                        </Badge>
                      </TableCell>
                      <TableCell>
                        {e.url ? (
                          <a href={e.url} className="hover:underline">
                            {e.title}
                          </a>
                        ) : (
                          e.title
                        )}
                        {e.description && (
                          <p className="text-xs text-muted-foreground">{e.description}</p>
                        )}
                      </TableCell>
                      <TableCell className="text-sm">{e.causer ?? "-"}</TableCell>
                      <TableCell className="text-sm">{e.package ?? "-"}</TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            )}
          </CardContent>
        </Card>
      )}

      <ServersAdmin />
    </div>
  );
}

function ServersAdmin() {
  const [servers, setServers] = React.useState<AdminGameServerInfo[] | null>(null);
  const [error, setError] = React.useState<string | null>(null);
  const [busyId, setBusyId] = React.useState<number | null>(null);

  const load = React.useCallback(() => {
    setError(null);
    api
      .adminListServers()
      .then((r) => setServers(r.servers))
      .catch((err) => setError(err instanceof Error ? err.message : "加载失败"));
  }, []);

  React.useEffect(() => {
    load();
  }, [load]);

  async function toggle(s: AdminGameServerInfo, field: "verified" | "listed") {
    setBusyId(s.id);
    try {
      await api.adminReviewServer(s.id, { [field]: !s[field] });
      setServers((prev) =>
        prev?.map((x) => (x.id === s.id ? { ...x, [field]: !x[field] } : x)) ?? prev,
      );
    } catch (err) {
      setError(err instanceof Error ? err.message : "操作失败");
    } finally {
      setBusyId(null);
    }
  }

  return (
    <Card>
      <CardHeader className="flex flex-row items-center justify-between">
        <CardTitle className="flex items-center gap-2 text-base">
          <Server className="size-4" /> 服务器收录
        </CardTitle>
        <Button variant="outline" size="sm" onClick={load}>
          <RefreshCw /> 刷新
        </Button>
      </CardHeader>
      <CardContent>
        {error ? (
          <div className="rounded-lg border border-destructive/40 bg-destructive/5 p-4 text-sm text-destructive">
            {error}
          </div>
        ) : servers === null ? (
          <div className="h-32 animate-pulse rounded-md bg-muted" />
        ) : servers.length === 0 ? (
          <p className="text-sm text-muted-foreground">还没有服务器注册。</p>
        ) : (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>地址</TableHead>
                <TableHead>名称</TableHead>
                <TableHead>服主</TableHead>
                <TableHead>在线</TableHead>
                <TableHead>认证</TableHead>
                <TableHead>上架</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {servers.map((s) => (
                <TableRow key={s.id}>
                  <TableCell className="text-sm">{s.address}</TableCell>
                  <TableCell className="text-sm">{s.name}</TableCell>
                  <TableCell className="text-sm">{s.owner}</TableCell>
                  <TableCell className="text-sm">
                    {s.online ? `${s.playersOnline}/${s.playersMax}` : "-"}
                  </TableCell>
                  <TableCell>
                    <Button
                      size="sm"
                      variant={s.verified ? "default" : "outline"}
                      disabled={busyId === s.id}
                      onClick={() => toggle(s, "verified")}
                    >
                      {s.verified ? "已认证" : "未认证"}
                    </Button>
                  </TableCell>
                  <TableCell>
                    <Button
                      size="sm"
                      variant={s.listed ? "default" : "outline"}
                      disabled={busyId === s.id}
                      onClick={() => toggle(s, "listed")}
                    >
                      {s.listed ? "已上架" : "未上架"}
                    </Button>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </CardContent>
    </Card>
  );
}
