"use client";

import * as React from "react";
import { api, type VaultEntry } from "@/lib/api";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { useToast } from "@/components/ui/toast";
import { Check, Copy, Eye, Loader2, Pencil, Plus, Trash2 } from "lucide-react";

export function VaultTab() {
  const { toast } = useToast();
  const [entries, setEntries] = React.useState<VaultEntry[] | null>(null);
  const [error, setError] = React.useState<string | null>(null);

  const [address, setAddress] = React.useState("");
  const [username, setUsername] = React.useState("");
  const [password, setPassword] = React.useState("");
  const [busy, setBusy] = React.useState(false);

  const load = React.useCallback(() => {
    api
      .listVault()
      .then((r) => {
        setEntries(r.entries);
        setError(null);
      })
      .catch((e) => setError(e instanceof Error ? e.message : "加载失败"));
  }, []);

  React.useEffect(() => {
    load();
  }, [load]);

  async function add(e: React.FormEvent) {
    e.preventDefault();
    if (!address.trim() || !username.trim() || !password) {
      toast("请填写完整:地址、用户名、密码", "error");
      return;
    }
    setBusy(true);
    try {
      await api.addVaultEntry({ address: address.trim(), username: username.trim(), password });
      toast("已添加");
      setAddress("");
      setUsername("");
      setPassword("");
      load();
    } catch (err) {
      toast(err instanceof Error ? err.message : "添加失败", "error");
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="space-y-6">
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
          <CardTitle className="text-base">手动添加凭证</CardTitle>
        </CardHeader>
        <CardContent>
          <form onSubmit={add} className="grid gap-3 sm:grid-cols-[1fr_1fr_1fr_auto] sm:items-end">
            <div className="space-y-1.5">
              <Label>服务器地址</Label>
              <Input
                value={address}
                onChange={(e) => setAddress(e.target.value)}
                placeholder="play.example.com:30000"
              />
            </div>
            <div className="space-y-1.5">
              <Label>用户名</Label>
              <Input value={username} onChange={(e) => setUsername(e.target.value)} />
            </div>
            <div className="space-y-1.5">
              <Label>密码</Label>
              <Input value={password} onChange={(e) => setPassword(e.target.value)} />
            </div>
            <Button type="submit" disabled={busy}>
              {busy ? <Loader2 className="animate-spin" /> : <Plus />} 添加
            </Button>
          </form>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle className="text-base">已保存的服务器凭证</CardTitle>
        </CardHeader>
        <CardContent>
          {entries === null && !error ? (
            <div className="h-16 animate-pulse rounded-md bg-muted" />
          ) : entries && entries.length === 0 ? (
            <p className="text-sm text-muted-foreground">
              还没有凭证。客户端进新服务器时会自动保存到这里。
            </p>
          ) : entries ? (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>服务器</TableHead>
                  <TableHead>用户名</TableHead>
                  <TableHead>密码</TableHead>
                  <TableHead>更新时间</TableHead>
                  <TableHead className="text-right">操作</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {entries.map((v) => (
                  <VaultRow key={v.id} entry={v} onChanged={load} />
                ))}
              </TableBody>
            </Table>
          ) : null}
        </CardContent>
      </Card>
    </div>
  );
}

function VaultRow({ entry, onChanged }: { entry: VaultEntry; onChanged: () => void }) {
  const { toast } = useToast();
  const [revealed, setRevealed] = React.useState<string | null>(null);
  const [copied, setCopied] = React.useState(false);
  const [editing, setEditing] = React.useState(false);
  const [username, setUsername] = React.useState(entry.username);
  const [password, setPassword] = React.useState("");
  const [busy, setBusy] = React.useState(false);

  async function reveal() {
    if (revealed !== null) {
      setRevealed(null);
      return;
    }
    try {
      const res = await api.revealVaultEntry(entry.id);
      setRevealed(res.password);
    } catch (err) {
      toast(err instanceof Error ? err.message : "查看失败", "error");
    }
  }

  async function copy() {
    if (revealed === null) return;
    try {
      await navigator.clipboard.writeText(revealed);
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    } catch {
      /* ignore */
    }
  }

  async function save(e: React.FormEvent) {
    e.preventDefault();
    setBusy(true);
    try {
      await api.updateVaultEntry(entry.id, {
        username: username.trim(),
        ...(password ? { password } : {}),
      });
      toast("已保存");
      setPassword("");
      setEditing(false);
      onChanged();
    } catch (err) {
      toast(err instanceof Error ? err.message : "保存失败", "error");
    } finally {
      setBusy(false);
    }
  }

  async function remove() {
    if (!confirm(`删除 ${entry.address} 的凭证?客户端将无法再自动登录该服务器。`)) return;
    try {
      await api.deleteVaultEntry(entry.id);
      toast("已删除");
      onChanged();
    } catch (err) {
      toast(err instanceof Error ? err.message : "删除失败", "error");
    }
  }

  if (editing) {
    return (
      <TableRow>
        <TableCell>
          <code className="text-xs">{entry.address}</code>
        </TableCell>
        <TableCell colSpan={3}>
          <form onSubmit={save} className="flex items-center gap-2">
            <Input
              value={username}
              onChange={(e) => setUsername(e.target.value)}
              className="h-8 w-36"
            />
            <Input
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              className="h-8 w-36"
              placeholder="新密码(留空不改)"
            />
            <Button size="sm" type="submit" disabled={busy}>
              {busy ? <Loader2 className="animate-spin" /> : null} 保存
            </Button>
            <Button size="sm" variant="ghost" type="button" onClick={() => setEditing(false)}>
              取消
            </Button>
          </form>
        </TableCell>
        <TableCell />
      </TableRow>
    );
  }

  return (
    <TableRow>
      <TableCell>
        <div className="flex items-center gap-2">
          <code className="text-xs font-medium">{entry.address}</code>
          {entry.status === "NEEDS_UPDATE" && (
            <Badge variant="destructive" title="客户端上报登录失败,密码可能已在别处被修改">
              待更新
            </Badge>
          )}
        </div>
      </TableCell>
      <TableCell>{entry.username}</TableCell>
      <TableCell>
        <div className="flex items-center gap-1">
          <Button size="sm" variant="ghost" onClick={reveal}>
            <Eye />
          </Button>
          {revealed !== null && (
            <>
              <code className="max-w-24 truncate rounded bg-muted px-1.5 py-0.5 text-xs">
                {revealed}
              </code>
              <Button size="sm" variant="ghost" onClick={copy}>
                {copied ? <Check /> : <Copy />}
              </Button>
            </>
          )}
        </div>
      </TableCell>
      <TableCell className="text-xs text-muted-foreground">
        {new Date(entry.updatedAt).toLocaleString("zh-CN")}
      </TableCell>
      <TableCell className="text-right">
        <div className="flex justify-end gap-1">
          <Button size="sm" variant="ghost" onClick={() => setEditing(true)}>
            <Pencil />
          </Button>
          <Button size="sm" variant="ghost" onClick={remove}>
            <Trash2 />
          </Button>
        </div>
      </TableCell>
    </TableRow>
  );
}
