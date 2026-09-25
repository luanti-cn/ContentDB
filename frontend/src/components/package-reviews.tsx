"use client";

import * as React from "react";
import { api, type ReviewInfo } from "@/lib/api";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import { Label } from "@/components/ui/label";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { useToast } from "@/components/ui/toast";
import { Star, ThumbsUp, ThumbsDown, Loader2, MessageSquare } from "lucide-react";

export function PackageReviews({ author, name }: { author: string; name: string }) {
  const { toast } = useToast();
  const [reviews, setReviews] = React.useState<ReviewInfo[] | null>(null);
  const [authed, setAuthed] = React.useState(false);

  const load = React.useCallback(() => {
    api
      .listReviews(author, name)
      .then(setReviews)
      .catch(() => setReviews([]));
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
        <CardTitle className="flex items-center gap-2 text-base">
          <MessageSquare className="size-4" /> 评价
        </CardTitle>
      </CardHeader>
      <CardContent className="space-y-4">
        {authed ? (
          <ReviewForm author={author} name={name} onDone={load} />
        ) : (
          <p className="text-sm text-muted-foreground">
            <a href="/login" className="text-primary hover:underline">
              登录
            </a>{" "}
            后可发表评价。
          </p>
        )}

        {reviews === null ? (
          <div className="h-16 animate-pulse rounded-md bg-muted" />
        ) : reviews.length === 0 ? (
          <p className="text-sm text-muted-foreground">还没有评价。</p>
        ) : (
          <div className="space-y-3">
            {reviews.map((r, i) => (
              <ReviewRow key={r.id ?? i} review={r} onVoted={load} />
            ))}
          </div>
        )}
      </CardContent>
    </Card>
  );
}

function Stars({ rating, onSelect }: { rating: number; onSelect?: (n: number) => void }) {
  return (
    <div className="flex items-center gap-0.5">
      {[1, 2, 3, 4, 5].map((n) => (
        <button
          key={n}
          type="button"
          onClick={onSelect ? () => onSelect(n) : undefined}
          className={onSelect ? "cursor-pointer" : "cursor-default"}
          aria-label={`${n} 星`}
        >
          <Star
            className={
              n <= rating ? "size-4 fill-yellow-400 text-yellow-400" : "size-4 text-muted-foreground"
            }
          />
        </button>
      ))}
    </div>
  );
}

function ReviewRow({ review, onVoted }: { review: ReviewInfo; onVoted: () => void }) {
  const { toast } = useToast();
  const [busy, setBusy] = React.useState(false);

  async function vote(isPositive: boolean) {
    if (review.id == null) return;
    setBusy(true);
    try {
      await api.voteReview(review.id, isPositive);
      toast("已投票");
      onVoted();
    } catch (e) {
      toast(e instanceof Error ? e.message : "投票失败", "error");
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="space-y-1.5 rounded-md border p-3">
      <div className="flex items-center justify-between gap-2">
        <span className="text-sm font-medium">{review.author ?? "匿名"}</span>
        {typeof review.rating === "number" && <Stars rating={review.rating} />}
      </div>
      {review.title && <p className="text-sm font-medium">{review.title}</p>}
      {review.comment && (
        <p className="whitespace-pre-wrap text-sm text-muted-foreground">{review.comment}</p>
      )}
      {review.id != null && (
        <div className="flex items-center gap-1 pt-1">
          <Button variant="ghost" size="sm" onClick={() => vote(true)} disabled={busy}>
            <ThumbsUp />
          </Button>
          <Button variant="ghost" size="sm" onClick={() => vote(false)} disabled={busy}>
            <ThumbsDown />
          </Button>
        </div>
      )}
    </div>
  );
}

function ReviewForm({
  author,
  name,
  onDone,
}: {
  author: string;
  name: string;
  onDone: () => void;
}) {
  const { toast } = useToast();
  const [rating, setRating] = React.useState(5);
  const [title, setTitle] = React.useState("");
  const [comment, setComment] = React.useState("");
  const [busy, setBusy] = React.useState(false);

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    if (!title.trim() || !comment.trim()) {
      toast("标题和内容必填", "error");
      return;
    }
    setBusy(true);
    try {
      await api.submitReview(author, name, { rating, title, comment });
      toast("已提交评价");
      setTitle("");
      setComment("");
      onDone();
    } catch (err) {
      toast(err instanceof Error ? err.message : "提交失败", "error");
    } finally {
      setBusy(false);
    }
  }

  async function remove() {
    if (!confirm("删除你的评价?")) return;
    setBusy(true);
    try {
      await api.deleteReview(author, name);
      toast("已删除");
      onDone();
    } catch (err) {
      toast(err instanceof Error ? err.message : "删除失败", "error");
    } finally {
      setBusy(false);
    }
  }

  return (
    <form onSubmit={submit} className="space-y-3 rounded-md border p-3">
      <div className="space-y-1.5">
        <Label>评分</Label>
        <Stars rating={rating} onSelect={setRating} />
      </div>
      <div className="space-y-1.5">
        <Label>标题</Label>
        <Input value={title} onChange={(e) => setTitle(e.target.value)} />
      </div>
      <div className="space-y-1.5">
        <Label>内容</Label>
        <Textarea value={comment} onChange={(e) => setComment(e.target.value)} rows={3} />
      </div>
      <div className="flex items-center gap-2">
        <Button type="submit" size="sm" disabled={busy}>
          {busy ? <Loader2 className="animate-spin" /> : null} 提交评价
        </Button>
        <Button type="button" variant="ghost" size="sm" onClick={remove} disabled={busy}>
          删除我的评价
        </Button>
      </div>
    </form>
  );
}
