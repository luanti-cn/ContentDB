"use client";

import * as React from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { api } from "@/lib/api";
import { Button } from "@/components/ui/button";
import { Select } from "@/components/ui/select";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { useToast } from "@/components/ui/toast";
import { Pencil, Images, Package, Trash2, Loader2, ShieldCheck } from "lucide-react";

const STATES = [
  { value: "wip", label: "开发中 (WIP)" },
  { value: "ready_for_review", label: "提交审核" },
  { value: "approved", label: "已批准 (Approved)" },
  { value: "changes_needed", label: "需要修改" },
  { value: "deleted", label: "删除" },
];

/**
 * 包所有者 / 管理员操作面板。
 * 仅当当前登录用户是包作者,或 rank 为 EDITOR+ 时显示。
 */
export function PackageOwnerActions({
  author,
  name,
}: {
  author: string;
  name: string;
}) {
  const router = useRouter();
  const { toast } = useToast();
  const [allowed, setAllowed] = React.useState<boolean | null>(null);
  const [isModerator, setIsModerator] = React.useState(false);
  const [state, setState] = React.useState("ready_for_review");
  const [busy, setBusy] = React.useState(false);

  React.useEffect(() => {
    let alive = true;
    api
      .whoami()
      .then((r) => {
        if (!alive) return;
        const rank = (r.rank ?? "").toUpperCase();
        const mod = ["EDITOR", "MODERATOR", "ADMIN"].some((x) => rank.includes(x));
        const owner = r.is_authenticated && r.username === author;
        setIsModerator(mod);
        setAllowed(Boolean(owner || mod));
      })
      .catch(() => alive && setAllowed(false));
    return () => {
      alive = false;
    };
  }, [author]);

  async function onMoveState() {
    setBusy(true);
    try {
      await api.movePackageState(author, name, state);
      toast(`状态已切换为 ${state}`);
      router.refresh();
    } catch (e) {
      toast(e instanceof Error ? e.message : "操作失败", "error");
    } finally {
      setBusy(false);
    }
  }

  async function onDelete() {
    if (!confirm("确定删除该包?")) return;
    setBusy(true);
    try {
      await api.deletePackage(author, name);
      toast("已删除");
      router.push("/");
    } catch (e) {
      toast(e instanceof Error ? e.message : "删除失败", "error");
      setBusy(false);
    }
  }

  if (!allowed) return null;

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">管理</CardTitle>
      </CardHeader>
      <CardContent className="space-y-3">
        <div className="grid grid-cols-1 gap-2">
          <Button asChild variant="outline" size="sm">
            <Link href={`/packages/${author}/${name}/edit`}>
              <Pencil /> 编辑信息
            </Link>
          </Button>
          <Button asChild variant="outline" size="sm">
            <Link href={`/packages/${author}/${name}/manage/releases`}>
              <Package /> 发布管理
            </Link>
          </Button>
          <Button asChild variant="outline" size="sm">
            <Link href={`/packages/${author}/${name}/manage/screenshots`}>
              <Images /> 截图管理
            </Link>
          </Button>
        </div>

        <div className="space-y-2 rounded-md border p-3">
          <div className="flex items-center gap-1.5 text-sm font-medium">
            <ShieldCheck className="size-4" /> 审核状态
          </div>
          <Select value={state} onChange={(e) => setState(e.target.value)}>
            {STATES.filter((s) =>
              // 非管理员不允许直接 approved
              isModerator ? true : s.value !== "approved"
            ).map((s) => (
              <option key={s.value} value={s.value}>
                {s.label}
              </option>
            ))}
          </Select>
          <Button className="w-full" size="sm" onClick={onMoveState} disabled={busy}>
            {busy ? <Loader2 className="animate-spin" /> : null} 应用状态
          </Button>
        </div>

        <Button
          variant="destructive"
          size="sm"
          className="w-full"
          onClick={onDelete}
          disabled={busy}
        >
          <Trash2 /> 删除包
        </Button>
      </CardContent>
    </Card>
  );
}
