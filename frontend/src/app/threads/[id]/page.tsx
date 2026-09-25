"use client";

import * as React from "react";
import Link from "next/link";
import { useParams } from "next/navigation";
import { api, type ThreadDetail } from "@/lib/api";
import { Button } from "@/components/ui/button";
import { Textarea } from "@/components/ui/textarea";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { useToast } from "@/components/ui/toast";
import { MessageSquare, Loader2, Lock, Send } from "lucide-react";

export default function ThreadPage() {
  const { id } = useParams<{ id: string }>();
  const threadId = Number(id);
  const { toast } = useToast();
  const [thread, setThread] = React.useState<ThreadDetail | null>(null);
  const [error, setError] = React.useState<string | null>(null);
  const [comment, setComment] = React.useState("");
  const [busy, setBusy] = React.useState(false);

  const load = React.useCallback(() => {
    api
      .getThread(threadId)
      .then((t) => {
        setThread(t);
        setError(null);
      })
      .catch((e) => setError(e instanceof Error ? e.message : "加载失败"));
  }, [threadId]);

  React.useEffect(() => {
    load();
  }, [load]);

  async function reply(e: React.FormEvent) {
    e.preventDefault();
    if (!comment.trim()) return;
    setBusy(true);
    try {
      await api.replyThread(threadId, comment.trim());
      setComment("");
      toast("已回复");
      load();
    } catch (err) {
      toast(err instanceof Error ? err.message : "回复失败", "error");
    } finally {
      setBusy(false);
    }
  }

  if (error) {
    return (
      <div className="mx-auto max-w-2xl rounded-lg border border-destructive/40 bg-destructive/5 p-4 text-sm text-destructive">
        {error}
      </div>
    );
  }
  if (!thread) {
    return <div className="mx-auto h-40 max-w-2xl animate-pulse rounded-lg bg-muted" />;
  }

  return (
    <div className="mx-auto max-w-2xl space-y-6">
      <div className="space-y-2">
        <div className="flex flex-wrap items-center gap-2">
          <MessageSquare className="size-5 text-primary" />
          <h1 className="text-2xl font-bold tracking-tight">{thread.title}</h1>
          {thread.is_private && <Badge variant="secondary">私有</Badge>}
          {thread.locked && (
            <Badge variant="outline">
              <Lock className="mr-1 size-3" /> 已锁定
            </Badge>
          )}
        </div>
        <p className="text-sm text-muted-foreground">
          由 {thread.author} 创建
          {thread.package && (
            <>
              {" · "}
              <Link href={`/packages/${thread.package}/`} className="hover:underline">
                {thread.package}
              </Link>
            </>
          )}
        </p>
      </div>

      <div className="space-y-3">
        {thread.replies.map((r) => (
          <Card key={r.id} className={r.is_status_update ? "border-dashed opacity-80" : ""}>
            <CardHeader className="pb-2">
              <CardTitle className="flex items-center justify-between text-sm font-medium">
                <span>{r.author}</span>
                <span className="text-xs font-normal text-muted-foreground">
                  {new Date(r.created_at).toLocaleString("zh-CN")}
                </span>
              </CardTitle>
            </CardHeader>
            <CardContent>
              <p className="whitespace-pre-wrap text-sm">{r.comment}</p>
            </CardContent>
          </Card>
        ))}
      </div>

      {thread.can_comment ? (
        <form onSubmit={reply} className="space-y-3">
          <Textarea
            value={comment}
            onChange={(e) => setComment(e.target.value)}
            rows={4}
            placeholder="写下你的回复…"
          />
          <div className="flex justify-end">
            <Button type="submit" disabled={busy}>
              {busy ? <Loader2 className="animate-spin" /> : <Send />} 回复
            </Button>
          </div>
        </form>
      ) : thread.locked ? (
        <p className="text-sm text-muted-foreground">该线程已锁定,无法回复。</p>
      ) : (
        <p className="text-sm text-muted-foreground">
          <a href="/login" className="text-primary hover:underline">
            登录
          </a>{" "}
          后可回复。
        </p>
      )}
    </div>
  );
}
