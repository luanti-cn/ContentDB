"use client";

import * as React from "react";
import Link from "next/link";
import { useParams } from "next/navigation";
import { api, type ReleaseInfo } from "@/lib/api";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
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
import { Tabs, TabsList, TabsTrigger, TabsContent } from "@/components/ui/tabs";
import { useToast } from "@/components/ui/toast";
import { ArrowLeft, Loader2, RefreshCw, Trash2, Check, Upload, GitBranch } from "lucide-react";

function formatSize(bytes: number): string {
  if (!bytes) return "-";
  const kb = bytes / 1024;
  return kb > 1024 ? `${(kb / 1024).toFixed(1)} MB` : `${Math.round(kb)} KB`;
}

export default function ReleasesManagePage() {
  const { author, name } = useParams<{ author: string; name: string }>();
  const { toast } = useToast();
  const [releases, setReleases] = React.useState<ReleaseInfo[] | null>(null);
  const [busy, setBusy] = React.useState<number | null>(null);

  const load = React.useCallback(() => {
    api
      .listReleases(author, name)
      .then(setReleases)
      .catch(() => setReleases([]));
  }, [author, name]);

  React.useEffect(() => {
    load();
  }, [load]);

  async function onDelete(id: number) {
    if (!confirm("确定删除该发布?")) return;
    setBusy(id);
    try {
      await api.deleteRelease(author, name, id);
      toast("已删除");
      load();
    } catch (e) {
      toast(e instanceof Error ? e.message : "删除失败", "error");
    } finally {
      setBusy(null);
    }
  }

  async function onApprove(id: number) {
    setBusy(id);
    try {
      await api.approveRelease(author, name, id);
      toast("已批准");
      load();
    } catch (e) {
      toast(e instanceof Error ? e.message : "批准失败", "error");
    } finally {
      setBusy(null);
    }
  }

  return (
    <div className="mx-auto max-w-4xl space-y-6">
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-2">
          <Button asChild variant="ghost" size="icon">
            <Link href={`/packages/${author}/${name}/`}>
              <ArrowLeft />
            </Link>
          </Button>
          <h1 className="text-2xl font-bold tracking-tight">
            发布管理 · {author}/{name}
          </h1>
        </div>
        <Button variant="outline" size="sm" onClick={load}>
          <RefreshCw /> 刷新
        </Button>
      </div>

      <Card>
        <CardHeader>
          <CardTitle className="text-base">新建发布</CardTitle>
        </CardHeader>
        <CardContent>
          <Tabs defaultValue="zip">
            <TabsList>
              <TabsTrigger value="zip">上传 ZIP</TabsTrigger>
              <TabsTrigger value="git">Git 导入</TabsTrigger>
            </TabsList>
            <TabsContent value="zip">
              <ZipUploadForm author={author} name={name} onDone={load} />
            </TabsContent>
            <TabsContent value="git">
              <GitImportForm author={author} name={name} onDone={load} />
            </TabsContent>
          </Tabs>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle className="text-base">已有发布</CardTitle>
        </CardHeader>
        <CardContent>
          {releases === null ? (
            <div className="h-24 animate-pulse rounded-md bg-muted" />
          ) : releases.length === 0 ? (
            <p className="text-sm text-muted-foreground">暂无发布。</p>
          ) : (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>标题</TableHead>
                  <TableHead>日期</TableHead>
                  <TableHead>大小</TableHead>
                  <TableHead className="text-right">操作</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {releases.map((r) => (
                  <TableRow key={r.id}>
                    <TableCell className="font-medium">{r.title || r.name}</TableCell>
                    <TableCell>
                      {new Date(r.release_date).toLocaleDateString("zh-CN")}
                    </TableCell>
                    <TableCell>{formatSize(r.size)}</TableCell>
                    <TableCell className="text-right">
                      <div className="flex justify-end gap-1">
                        <Button
                          variant="ghost"
                          size="sm"
                          onClick={() => onApprove(r.id)}
                          disabled={busy === r.id}
                          title="批准"
                        >
                          <Check />
                        </Button>
                        <Button
                          variant="ghost"
                          size="sm"
                          onClick={() => onDelete(r.id)}
                          disabled={busy === r.id}
                          title="删除"
                        >
                          {busy === r.id ? <Loader2 className="animate-spin" /> : <Trash2 />}
                        </Button>
                      </div>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}
        </CardContent>
      </Card>
    </div>
  );
}

function ZipUploadForm({
  author,
  name,
  onDone,
}: {
  author: string;
  name: string;
  onDone: () => void;
}) {
  const { toast } = useToast();
  const [file, setFile] = React.useState<File | null>(null);
  const [title, setTitle] = React.useState("");
  const [relName, setRelName] = React.useState("");
  const [notes, setNotes] = React.useState("");
  const [busy, setBusy] = React.useState(false);

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    if (!file) {
      toast("请选择 zip 文件", "error");
      return;
    }
    const form = new FormData();
    form.append("file", file);
    form.append("name", relName || title || "release");
    form.append("title", title || relName || "Release");
    form.append("release_notes", notes);
    setBusy(true);
    try {
      await api.uploadZipRelease(author, name, form);
      toast("上传成功");
      setFile(null);
      setTitle("");
      setRelName("");
      setNotes("");
      onDone();
    } catch (err) {
      toast(err instanceof Error ? err.message : "上传失败", "error");
    } finally {
      setBusy(false);
    }
  }

  return (
    <form onSubmit={submit} className="space-y-4">
      <div className="space-y-1.5">
        <Label>ZIP 文件</Label>
        <Input
          type="file"
          accept=".zip"
          onChange={(e) => setFile(e.target.files?.[0] ?? null)}
        />
      </div>
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
        <div className="space-y-1.5">
          <Label>版本标题 (title)</Label>
          <Input value={title} onChange={(e) => setTitle(e.target.value)} placeholder="1.0.0" />
        </div>
        <div className="space-y-1.5">
          <Label>版本名 (name)</Label>
          <Input value={relName} onChange={(e) => setRelName(e.target.value)} placeholder="1.0.0" />
        </div>
      </div>
      <div className="space-y-1.5">
        <Label>发布说明 (release notes)</Label>
        <Textarea value={notes} onChange={(e) => setNotes(e.target.value)} rows={3} />
      </div>
      <Button type="submit" disabled={busy}>
        {busy ? <Loader2 className="animate-spin" /> : <Upload />} 上传
      </Button>
    </form>
  );
}

function GitImportForm({
  author,
  name,
  onDone,
}: {
  author: string;
  name: string;
  onDone: () => void;
}) {
  const { toast } = useToast();
  const [ref, setRef] = React.useState("");
  const [title, setTitle] = React.useState("");
  const [relName, setRelName] = React.useState("");
  const [notes, setNotes] = React.useState("");
  const [busy, setBusy] = React.useState(false);

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    if (!ref.trim()) {
      toast("请填写 git ref(分支/标签/commit)", "error");
      return;
    }
    setBusy(true);
    try {
      await api.createGitRelease(author, name, {
        ref: ref.trim(),
        name: relName || undefined,
        title: title || undefined,
        releaseNotes: notes || undefined,
      });
      toast("已提交 Git 导入,后台打包中(稍后刷新查看)");
      setRef("");
      setTitle("");
      setRelName("");
      setNotes("");
      onDone();
    } catch (err) {
      toast(err instanceof Error ? err.message : "导入失败", "error");
    } finally {
      setBusy(false);
    }
  }

  return (
    <form onSubmit={submit} className="space-y-4">
      <div className="space-y-1.5">
        <Label>Git Ref(分支 / 标签 / commit)</Label>
        <Input value={ref} onChange={(e) => setRef(e.target.value)} placeholder="main 或 v1.0.0" />
      </div>
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
        <div className="space-y-1.5">
          <Label>版本标题 (title)</Label>
          <Input value={title} onChange={(e) => setTitle(e.target.value)} />
        </div>
        <div className="space-y-1.5">
          <Label>版本名 (name)</Label>
          <Input value={relName} onChange={(e) => setRelName(e.target.value)} />
        </div>
      </div>
      <div className="space-y-1.5">
        <Label>发布说明 (release notes)</Label>
        <Textarea value={notes} onChange={(e) => setNotes(e.target.value)} rows={3} />
      </div>
      <p className="text-xs text-muted-foreground">
        导入后状态为 PROCESSING,后台 worker 打包完成后可在上方列表看到。
      </p>
      <Button type="submit" disabled={busy}>
        {busy ? <Loader2 className="animate-spin" /> : <GitBranch />} 从 Git 导入
      </Button>
    </form>
  );
}
