"use client";

import * as React from "react";
import Link from "next/link";
import { useParams } from "next/navigation";
import { api, type ScreenshotInfo } from "@/lib/api";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { useToast } from "@/components/ui/toast";
import {
  ArrowLeft,
  Loader2,
  RefreshCw,
  Trash2,
  Upload,
  Star,
  ArrowUp,
  ArrowDown,
} from "lucide-react";

export default function ScreenshotsManagePage() {
  const { author, name } = useParams<{ author: string; name: string }>();
  const { toast } = useToast();
  const [shots, setShots] = React.useState<ScreenshotInfo[] | null>(null);
  const [busy, setBusy] = React.useState<number | null>(null);

  const load = React.useCallback(() => {
    api
      .listScreenshots(author, name)
      .then((s) => setShots([...s].sort((a, b) => a.order - b.order)))
      .catch(() => setShots([]));
  }, [author, name]);

  React.useEffect(() => {
    load();
  }, [load]);

  async function onDelete(id: number) {
    if (!confirm("删除该截图?")) return;
    setBusy(id);
    try {
      await api.deleteScreenshot(author, name, id);
      toast("已删除");
      load();
    } catch (e) {
      toast(e instanceof Error ? e.message : "删除失败", "error");
    } finally {
      setBusy(null);
    }
  }

  async function onSetCover(id: number) {
    setBusy(id);
    try {
      await api.setCoverImage(author, name, id);
      toast("已设为封面");
      load();
    } catch (e) {
      toast(e instanceof Error ? e.message : "操作失败", "error");
    } finally {
      setBusy(null);
    }
  }

  async function move(index: number, dir: -1 | 1) {
    if (!shots) return;
    const next = [...shots];
    const target = index + dir;
    if (target < 0 || target >= next.length) return;
    [next[index], next[target]] = [next[target], next[index]];
    setShots(next);
    try {
      await api.orderScreenshots(
        author,
        name,
        next.map((s) => s.id)
      );
      toast("排序已保存");
    } catch (e) {
      toast(e instanceof Error ? e.message : "排序失败", "error");
      load();
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
            截图管理 · {author}/{name}
          </h1>
        </div>
        <Button variant="outline" size="sm" onClick={load}>
          <RefreshCw /> 刷新
        </Button>
      </div>

      <Card>
        <CardHeader>
          <CardTitle className="text-base">上传截图</CardTitle>
        </CardHeader>
        <CardContent>
          <UploadForm author={author} name={name} onDone={load} />
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle className="text-base">已有截图</CardTitle>
        </CardHeader>
        <CardContent>
          {shots === null ? (
            <div className="h-24 animate-pulse rounded-md bg-muted" />
          ) : shots.length === 0 ? (
            <p className="text-sm text-muted-foreground">暂无截图。</p>
          ) : (
            <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
              {shots.map((s, i) => (
                <div key={s.id} className="space-y-2 rounded-lg border p-3">
                  {/* eslint-disable-next-line @next/next/no-img-element */}
                  <img
                    src={s.url}
                    alt={s.title}
                    className="aspect-video w-full rounded-md border object-cover"
                    loading="lazy"
                  />
                  <div className="flex items-center justify-between gap-2">
                    <div className="flex items-center gap-2 text-sm">
                      <span className="truncate">{s.title || `#${s.id}`}</span>
                      {s.is_cover_image && (
                        <Badge variant="secondary" className="shrink-0">
                          封面
                        </Badge>
                      )}
                      {!s.approved && (
                        <Badge variant="outline" className="shrink-0">
                          待审
                        </Badge>
                      )}
                    </div>
                  </div>
                  <div className="flex items-center gap-1">
                    <Button
                      variant="ghost"
                      size="sm"
                      onClick={() => move(i, -1)}
                      disabled={i === 0}
                      title="上移"
                    >
                      <ArrowUp />
                    </Button>
                    <Button
                      variant="ghost"
                      size="sm"
                      onClick={() => move(i, 1)}
                      disabled={i === shots.length - 1}
                      title="下移"
                    >
                      <ArrowDown />
                    </Button>
                    <Button
                      variant="ghost"
                      size="sm"
                      onClick={() => onSetCover(s.id)}
                      disabled={busy === s.id || s.is_cover_image}
                      title="设为封面"
                    >
                      <Star />
                    </Button>
                    <Button
                      variant="ghost"
                      size="sm"
                      onClick={() => onDelete(s.id)}
                      disabled={busy === s.id}
                      title="删除"
                      className="ml-auto"
                    >
                      {busy === s.id ? <Loader2 className="animate-spin" /> : <Trash2 />}
                    </Button>
                  </div>
                </div>
              ))}
            </div>
          )}
        </CardContent>
      </Card>
    </div>
  );
}

function UploadForm({
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
  const [isCover, setIsCover] = React.useState(false);
  const [busy, setBusy] = React.useState(false);

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    if (!file) {
      toast("请选择图片", "error");
      return;
    }
    const form = new FormData();
    form.append("file", file);
    form.append("title", title);
    form.append("is_cover_image", isCover ? "true" : "false");
    setBusy(true);
    try {
      await api.uploadScreenshot(author, name, form);
      toast("上传成功");
      setFile(null);
      setTitle("");
      setIsCover(false);
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
        <Label>图片文件</Label>
        <Input
          type="file"
          accept="image/*"
          onChange={(e) => setFile(e.target.files?.[0] ?? null)}
        />
      </div>
      <div className="space-y-1.5">
        <Label>标题</Label>
        <Input value={title} onChange={(e) => setTitle(e.target.value)} placeholder="截图说明" />
      </div>
      <label className="flex items-center gap-2 text-sm">
        <input
          type="checkbox"
          checked={isCover}
          onChange={(e) => setIsCover(e.target.checked)}
          className="size-4"
        />
        设为封面图
      </label>
      <Button type="submit" disabled={busy}>
        {busy ? <Loader2 className="animate-spin" /> : <Upload />} 上传
      </Button>
    </form>
  );
}
