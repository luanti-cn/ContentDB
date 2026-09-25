"use client";

import * as React from "react";
import { api, type TokenInfo } from "@/lib/api";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { useToast } from "@/components/ui/toast";
import { KeyRound, Loader2, Plus, Trash2, Copy, Check } from "lucide-react";

export default function TokensPage() {
  const { toast } = useToast();
  const [tokens, setTokens] = React.useState<TokenInfo[] | null>(null);
  const [error, setError] = React.useState<string | null>(null);
  const [name, setName] = React.useState("");
  const [busy, setBusy] = React.useState(false);
  const [newSecret, setNewSecret] = React.useState<{ name: string; token: string } | null>(null);

  const load = React.useCallback(() => {
    api
      .listTokens()
      .then((t) => {
        setTokens(t);
        setError(null);
      })
      .catch((e) => setError(e instanceof Error ? e.message : "加载失败"));
  }, []);

  React.useEffect(() => {
    load();
  }, [load]);

  async function create(e: React.FormEvent) {
    e.preventDefault();
    if (!name.trim()) {
      toast("请填写 Token 名称", "error");
      return;
    }
    setBusy(true);
    try {
      const res = await api.createToken({ name: name.trim() });
      setNewSecret({ name: res.name, token: res.accessToken });
      setName("");
      toast("Token 已创建,请立即复制");
      load();
    } catch (err) {
      toast(err instanceof Error ? err.message : "创建失败", "error");
    } finally {
      setBusy(false);
    }
  }

  async function remove(id: number) {
    if (!confirm("删除该 Token?使用它的应用将失效。")) return;
    try {
      await api.deleteToken(id);
      toast("已删除");
      load();
    } catch (err) {
      toast(err instanceof Error ? err.message : "删除失败", "error");
    }
  }

  return (
    <div className="mx-auto max-w-2xl space-y-6">
      <div className="flex items-center gap-2">
        <KeyRound className="size-5 text-primary" />
        <h1 className="text-2xl font-bold tracking-tight">API Token</h1>
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

      {newSecret && (
        <SecretReveal
          name={newSecret.name}
          token={newSecret.token}
          onClose={() => setNewSecret(null)}
        />
      )}

      <Card>
        <CardHeader>
          <CardTitle className="text-base">创建新 Token</CardTitle>
        </CardHeader>
        <CardContent>
          <form onSubmit={create} className="flex items-end gap-3">
            <div className="flex-1 space-y-1.5">
              <Label>名称</Label>
              <Input
                value={name}
                onChange={(e) => setName(e.target.value)}
                placeholder="CI 部署 / 命令行工具"
              />
            </div>
            <Button type="submit" disabled={busy}>
              {busy ? <Loader2 className="animate-spin" /> : <Plus />} 创建
            </Button>
          </form>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle className="text-base">已有 Token</CardTitle>
        </CardHeader>
        <CardContent>
          {tokens === null && !error ? (
            <div className="h-16 animate-pulse rounded-md bg-muted" />
          ) : tokens && tokens.length === 0 ? (
            <p className="text-sm text-muted-foreground">还没有 Token。</p>
          ) : tokens ? (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>名称</TableHead>
                  <TableHead>创建时间</TableHead>
                  <TableHead className="text-right">操作</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {tokens.map((t) => (
                  <TableRow key={t.id}>
                    <TableCell className="font-medium">
                      {t.name}
                      {t.packageKey && (
                        <span className="ml-2 text-xs text-muted-foreground">
                          限 {t.packageKey}
                        </span>
                      )}
                    </TableCell>
                    <TableCell>
                      {t.createdAt ? new Date(t.createdAt).toLocaleDateString("zh-CN") : "-"}
                    </TableCell>
                    <TableCell className="text-right">
                      <Button variant="ghost" size="sm" onClick={() => remove(t.id)}>
                        <Trash2 />
                      </Button>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          ) : null}
        </CardContent>
      </Card>
    </div>
  );
}

function SecretReveal({
  name,
  token,
  onClose,
}: {
  name: string;
  token: string;
  onClose: () => void;
}) {
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
    <div className="space-y-3 rounded-lg border border-primary/40 bg-primary/5 p-4">
      <p className="text-sm font-medium">
        Token「{name}」已创建 — 明文只显示这一次,请立即复制保存:
      </p>
      <div className="flex items-center gap-2">
        <code className="flex-1 overflow-x-auto rounded-md border bg-background px-3 py-2 text-xs">
          {token}
        </code>
        <Button size="sm" variant="outline" onClick={copy}>
          {copied ? <Check /> : <Copy />} {copied ? "已复制" : "复制"}
        </Button>
      </div>
      <Button size="sm" variant="ghost" onClick={onClose}>
        我已保存,关闭
      </Button>
    </div>
  );
}
