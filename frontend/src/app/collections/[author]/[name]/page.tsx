"use client";

import * as React from "react";
import Link from "next/link";
import { useParams, useRouter } from "next/navigation";
import { api, type CollectionInfo } from "@/lib/api";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { useToast } from "@/components/ui/toast";
import { ArrowLeft, Loader2, Plus, Trash2, Library } from "lucide-react";

export default function CollectionDetailPage() {
  const { author, name } = useParams<{ author: string; name: string }>();
  const router = useRouter();
  const { toast } = useToast();
  const [col, setCol] = React.useState<CollectionInfo | null>(null);
  const [notFound, setNotFound] = React.useState(false);
  const [isOwner, setIsOwner] = React.useState(false);

  const load = React.useCallback(() => {
    api
      .getCollection(author, name)
      .then((c) => setCol(c))
      .catch(() => setNotFound(true));
  }, [author, name]);

  React.useEffect(() => {
    load();
    api
      .whoami()
      .then((r) => setIsOwner(r.is_authenticated && r.username === author))
      .catch(() => setIsOwner(false));
  }, [load, author]);

  async function removePackage(pAuthor: string, pName: string) {
    if (!confirm(`从合集移除 ${pAuthor}/${pName}?`)) return;
    try {
      await api.removeFromCollection(author, name, {
        packageAuthor: pAuthor,
        packageName: pName,
      });
      toast("已移除");
      load();
    } catch (e) {
      toast(e instanceof Error ? e.message : "移除失败", "error");
    }
  }

  async function onDelete() {
    if (!confirm("删除该合集?")) return;
    try {
      await api.deleteCollection(author, name);
      toast("已删除");
      router.push("/collections");
    } catch (e) {
      toast(e instanceof Error ? e.message : "删除失败", "error");
    }
  }

  if (notFound) {
    return <p className="text-sm text-muted-foreground">合集不存在。</p>;
  }
  if (!col) {
    return <div className="h-40 animate-pulse rounded-lg bg-muted" />;
  }

  const packages = col.packages ?? [];

  return (
    <div className="mx-auto max-w-4xl space-y-6">
      <div className="flex items-center gap-2">
        <Button asChild variant="ghost" size="icon">
          <Link href="/collections">
            <ArrowLeft />
          </Link>
        </Button>
        <Library className="size-5 text-primary" />
        <h1 className="text-2xl font-bold tracking-tight">{col.title}</h1>
      </div>

      <p className="text-muted-foreground">{col.short_description}</p>
      {col.long_description && (
        <p className="whitespace-pre-wrap text-sm text-muted-foreground">{col.long_description}</p>
      )}

      {isOwner && (
        <div className="flex flex-wrap items-center gap-3">
          <AddPackageForm author={author} name={name} onDone={load} />
          <Button variant="destructive" size="sm" onClick={onDelete}>
            <Trash2 /> 删除合集
          </Button>
        </div>
      )}

      <Card>
        <CardHeader>
          <CardTitle className="text-base">包 ({packages.length})</CardTitle>
        </CardHeader>
        <CardContent className="space-y-2">
          {packages.length === 0 ? (
            <p className="text-sm text-muted-foreground">合集里还没有包。</p>
          ) : (
            packages.map((p) => (
              <div
                key={`${p.author}/${p.name}`}
                className="flex items-center justify-between gap-3 rounded-md border p-3"
              >
                <div>
                  <Link
                    href={`/packages/${p.author}/${p.name}/`}
                    className="font-medium hover:underline"
                  >
                    {p.title || p.name}
                  </Link>
                  <p className="text-xs text-muted-foreground">
                    {p.author}
                    {p.short_description ? ` · ${p.short_description}` : ""}
                  </p>
                </div>
                {isOwner && (
                  <Button
                    variant="ghost"
                    size="sm"
                    onClick={() => removePackage(p.author, p.name)}
                  >
                    <Trash2 />
                  </Button>
                )}
              </div>
            ))
          )}
        </CardContent>
      </Card>
    </div>
  );
}

function AddPackageForm({
  author,
  name,
  onDone,
}: {
  author: string;
  name: string;
  onDone: () => void;
}) {
  const { toast } = useToast();
  const [pkgAuthor, setPkgAuthor] = React.useState("");
  const [pkgName, setPkgName] = React.useState("");
  const [busy, setBusy] = React.useState(false);

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    if (!pkgAuthor.trim() || !pkgName.trim()) {
      toast("请填写包作者和名称", "error");
      return;
    }
    setBusy(true);
    try {
      await api.addToCollection(author, name, {
        packageAuthor: pkgAuthor.trim(),
        packageName: pkgName.trim(),
      });
      toast("已添加");
      setPkgAuthor("");
      setPkgName("");
      onDone();
    } catch (err) {
      toast(err instanceof Error ? err.message : "添加失败", "error");
    } finally {
      setBusy(false);
    }
  }

  return (
    <form onSubmit={submit} className="flex items-end gap-2">
      <div className="space-y-1.5">
        <Label>包作者</Label>
        <Input value={pkgAuthor} onChange={(e) => setPkgAuthor(e.target.value)} className="w-32" />
      </div>
      <div className="space-y-1.5">
        <Label>包名称</Label>
        <Input value={pkgName} onChange={(e) => setPkgName(e.target.value)} className="w-40" />
      </div>
      <Button type="submit" size="sm" disabled={busy}>
        {busy ? <Loader2 className="animate-spin" /> : <Plus />} 添加
      </Button>
    </form>
  );
}
