"use client";

import * as React from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { api, type ThreadSummary } from "@/lib/api";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import { Label } from "@/components/ui/label";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { useToast } from "@/components/ui/toast";
import { MessagesSquare, Loader2, Plus, Lock } from "lucide-react";

export function PackageThreads({ author, name }: { author: string; name: string }) {
  const router = useRouter();
  const { toast } = useToast();
  const [threads, setThreads] = React.useState<ThreadSummary[] | null>(null);
  const [authed, setAuthed] = React.useState(false);
  const [showForm, setShowForm] = React.useState(false);

  const load = React.useCallback(() => {
    api
      .listThreads({ author, name })
      .then((t) => setThreads(Array.isArray(t) ? t : []))
      .catch(() => setThreads([]));
  }, [author, name]);

  React.useEffect(() => {
    load();
    api
      .whoami()
      .then((r) => setAuthed(r.is_authenticated))
      .catch(() => setAuthed(false));
  }, [load]);

  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center justify-between text-base">
          <span className="flex items-center gap-2">
            <MessagesSquare className="size-4" /> 讨论
          </span>
          {authed && (
            <Button size="sm" variant="outline" onClick={() => setShowForm((s) => !s)}>
              <Plus /> 新话题
            </Button>
          )}
        </CardTitle>
      </CardHeader>
      <CardContent className="space-y-4">
        {showForm && authed && (
          <NewThreadForm
            author={author}
            name={name}
            onCreated={(id) => {
              setShowForm(false);
              if (id) router.push(`/threads/${id}/`);
              else load();
            }}
          />
        )}

        {threads === null ? (
          <div className="h-16 animate-pulse rounded-md bg-muted" />
        ) : threads.length === 0 ? (
          <p className="text-sm text-muted-foreground">还没有讨论。</p>
        ) : (
          <div className="space-y-2">
            {threads.map((t) => (
              <Link
                key={t.id}
                href={`/threads/${t.id}/`}
                className="flex items-center justify-between gap-2 rounded-md border p-3 hover:bg-accent"
              >
                <div className="flex items-center gap-2">
                  <span className="text-sm font-medium">{t.title}</span>
                  {t.is_private && (
                    <Badge variant="secondary" className="shrink-0">
                      私有
                    </Badge>
                  )}
                  {t.locked && <Lock className="size-3.5 text-muted-foreground" />}
                </div>
                <span className="shrink-0 text-xs text-muted-foreground">{t.author}</span>
              </Link>
            ))}
          </div>
        )}
      </CardContent>
    </Card>
  );
}

function NewThreadForm({
  author,
  name,
  onCreated,
}: {
  author: string;
  name: string;
  onCreated: (id?: number) => void;
}) {
  const { toast } = useToast();
  const [title, setTitle] = React.useState("");
  const [comment, setComment] = React.useState("");
  const [isPrivate, setPrivate] = React.useState(false);
  const [busy, setBusy] = React.useState(false);

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    if (!title.trim() || !comment.trim()) {
      toast("标题和内容必填", "error");
      return;
    }
    setBusy(true);
    try {
      const res = await api.createThread({
        packageAuthor: author,
        packageName: name,
        title: title.trim(),
        comment: comment.trim(),
        private: isPrivate,
      });
      toast("已创建话题");
      onCreated(res?.id);
    } catch (err) {
      toast(err instanceof Error ? err.message : "创建失败", "error");
    } finally {
      setBusy(false);
    }
  }

  return (
    <form onSubmit={submit} className="space-y-3 rounded-md border p-3">
      <div className="space-y-1.5">
        <Label>标题</Label>
        <Input value={title} onChange={(e) => setTitle(e.target.value)} />
      </div>
      <div className="space-y-1.5">
        <Label>内容</Label>
        <Textarea value={comment} onChange={(e) => setComment(e.target.value)} rows={3} />
      </div>
      <label className="flex items-center gap-2 text-sm">
        <input
          type="checkbox"
          checked={isPrivate}
          onChange={(e) => setPrivate(e.target.checked)}
          className="size-4"
        />
        私有话题(仅维护者与审核可见)
      </label>
      <Button type="submit" size="sm" disabled={busy}>
        {busy ? <Loader2 className="animate-spin" /> : <Plus />} 创建
      </Button>
    </form>
  );
}
