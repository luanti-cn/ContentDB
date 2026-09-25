"use client";

import * as React from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { api, type CollectionInfo } from "@/lib/api";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import { Label } from "@/components/ui/label";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { useToast } from "@/components/ui/toast";
import { Library, Loader2, Plus } from "lucide-react";

export default function CollectionsPage() {
  const { toast } = useToast();
  const router = useRouter();
  const [items, setItems] = React.useState<CollectionInfo[] | null>(null);
  const [showForm, setShowForm] = React.useState(false);

  const load = React.useCallback(() => {
    api
      .listCollections()
      .then((c) => setItems(Array.isArray(c) ? c : []))
      .catch(() => setItems([]));
  }, []);

  React.useEffect(() => {
    load();
  }, [load]);

  return (
    <div className="mx-auto max-w-4xl space-y-6">
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-2">
          <Library className="size-5 text-primary" />
          <h1 className="text-2xl font-bold tracking-tight">合集</h1>
        </div>
        <Button size="sm" onClick={() => setShowForm((s) => !s)}>
          <Plus /> 新建合集
        </Button>
      </div>

      {showForm && (
        <CreateCollectionForm
          onDone={(author, name) => {
            setShowForm(false);
            if (author && name) router.push(`/collections/${author}/${name}/`);
            else load();
          }}
        />
      )}

      {items === null ? (
        <div className="space-y-3">
          {[0, 1, 2].map((i) => (
            <div key={i} className="h-20 animate-pulse rounded-lg bg-muted" />
          ))}
        </div>
      ) : items.length === 0 ? (
        <p className="text-sm text-muted-foreground">暂无合集。</p>
      ) : (
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
          {items.map((c) => (
            <Link key={`${c.author}/${c.name}`} href={`/collections/${c.author}/${c.name}/`}>
              <Card className="h-full transition-colors hover:border-primary/50">
                <CardHeader>
                  <CardTitle className="text-base">{c.title}</CardTitle>
                </CardHeader>
                <CardContent className="space-y-1">
                  <p className="text-sm text-muted-foreground">{c.short_description}</p>
                  <p className="text-xs text-muted-foreground">
                    by {c.author}
                    {typeof c.package_count === "number" ? ` · ${c.package_count} 个包` : ""}
                  </p>
                </CardContent>
              </Card>
            </Link>
          ))}
        </div>
      )}
    </div>
  );

  function CreateCollectionForm({
    onDone,
  }: {
    onDone: (author?: string, name?: string) => void;
  }) {
    const [name, setName] = React.useState("");
    const [title, setTitle] = React.useState("");
    const [shortDescription, setShort] = React.useState("");
    const [longDescription, setLong] = React.useState("");
    const [busy, setBusy] = React.useState(false);

    async function submit(e: React.FormEvent) {
      e.preventDefault();
      if (!name.trim() || !title.trim()) {
        toast("技术名称与标题必填", "error");
        return;
      }
      setBusy(true);
      try {
        await api.createCollection({
          name: name.trim(),
          title: title.trim(),
          shortDescription,
          longDescription: longDescription || undefined,
        });
        toast("合集已创建");
        const me = await api.whoami().catch(() => null);
        onDone(me?.username ?? undefined, name.trim());
      } catch (err) {
        toast(err instanceof Error ? err.message : "创建失败", "error");
      } finally {
        setBusy(false);
      }
    }

    return (
      <Card>
        <CardHeader>
          <CardTitle className="text-base">新建合集</CardTitle>
        </CardHeader>
        <CardContent>
          <form onSubmit={submit} className="space-y-4">
            <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
              <div className="space-y-1.5">
                <Label>技术名称 (name)</Label>
                <Input value={name} onChange={(e) => setName(e.target.value)} placeholder="my_list" />
              </div>
              <div className="space-y-1.5">
                <Label>标题 (title)</Label>
                <Input value={title} onChange={(e) => setTitle(e.target.value)} />
              </div>
            </div>
            <div className="space-y-1.5">
              <Label>简介</Label>
              <Input value={shortDescription} onChange={(e) => setShort(e.target.value)} />
            </div>
            <div className="space-y-1.5">
              <Label>详细描述 (Markdown)</Label>
              <Textarea value={longDescription} onChange={(e) => setLong(e.target.value)} rows={4} />
            </div>
            <Button type="submit" disabled={busy}>
              {busy ? <Loader2 className="animate-spin" /> : <Plus />} 创建
            </Button>
          </form>
        </CardContent>
      </Card>
    );
  }
}
