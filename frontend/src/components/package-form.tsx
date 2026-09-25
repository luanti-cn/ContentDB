"use client";

import * as React from "react";
import { useRouter } from "next/navigation";
import { api, type PackageWriteBody, type PackageDetail } from "@/lib/api";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import { Label } from "@/components/ui/label";
import { Select } from "@/components/ui/select";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { useToast } from "@/components/ui/toast";
import { Loader2, Save } from "lucide-react";

const TYPES = [
  { value: "mod", label: "Mod" },
  { value: "game", label: "子游戏 (Game)" },
  { value: "txp", label: "材质包 (Texture Pack)" },
];

/**
 * 包创建 / 编辑通用表单。
 * - mode="create":调 createPackage,成功后跳转到详情页
 * - mode="edit":调 editPackage(需 author/name 与 existing)
 */
export function PackageForm({
  mode,
  author,
  name: pkgName,
  existing,
}: {
  mode: "create" | "edit";
  author?: string;
  name?: string;
  existing?: PackageDetail | null;
}) {
  const router = useRouter();
  const { toast } = useToast();
  const [saving, setSaving] = React.useState(false);

  const [form, setForm] = React.useState<PackageWriteBody>({
    name: existing?.name ?? "",
    title: existing?.title ?? "",
    shortDescription: existing?.short_description ?? "",
    longDescription: existing?.long_description ?? "",
    type: existing?.type ?? "mod",
    license: existing?.license ?? "",
    mediaLicense: existing?.media_license ?? "",
    repo: existing?.repo ?? "",
    website: existing?.website ?? "",
    issueTracker: existing?.issue_tracker ?? "",
    donateUrl: existing?.donate_url ?? "",
    tags: existing?.tags ?? [],
  });

  const [tagsText, setTagsText] = React.useState((existing?.tags ?? []).join(", "));

  function set<K extends keyof PackageWriteBody>(key: K, value: PackageWriteBody[K]) {
    setForm((f) => ({ ...f, [key]: value }));
  }

  async function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!form.name?.trim()) {
      toast("技术名称 (name) 必填", "error");
      return;
    }
    setSaving(true);
    const body: PackageWriteBody = {
      ...form,
      tags: tagsText
        .split(",")
        .map((t) => t.trim())
        .filter(Boolean),
    };
    try {
      if (mode === "create") {
        await api.createPackage(body);
        toast("包已创建");
        // 创建后 author = 当前登录用户,查一次 whoami 以跳转到详情页
        const me = await api.whoami().catch(() => null);
        if (me?.username) {
          router.push(`/packages/${me.username}/${form.name}/`);
        } else {
          router.push("/");
        }
      } else {
        await api.editPackage(author!, pkgName!, body);
        toast("已保存");
        router.push(`/packages/${author}/${pkgName}/`);
      }
      router.refresh();
    } catch (err) {
      toast(err instanceof Error ? err.message : "保存失败", "error");
    } finally {
      setSaving(false);
    }
  }

  return (
    <form onSubmit={onSubmit} className="mx-auto max-w-3xl space-y-6">
      <h1 className="text-2xl font-bold tracking-tight">
        {mode === "create" ? "创建新包" : `编辑 ${existing?.title ?? pkgName}`}
      </h1>

      <Card>
        <CardHeader>
          <CardTitle className="text-base">基本信息</CardTitle>
        </CardHeader>
        <CardContent className="space-y-4">
          <Field label="技术名称 (name)" hint="仅小写字母/数字/下划线,创建后不建议修改">
            <Input
              value={form.name ?? ""}
              onChange={(e) => set("name", e.target.value)}
              placeholder="my_awesome_mod"
              disabled={mode === "edit"}
              required
            />
          </Field>
          <Field label="标题 (title)">
            <Input
              value={form.title ?? ""}
              onChange={(e) => set("title", e.target.value)}
              placeholder="My Awesome Mod"
            />
          </Field>
          <Field label="类型 (type)">
            <Select value={form.type ?? "mod"} onChange={(e) => set("type", e.target.value)}>
              {TYPES.map((t) => (
                <option key={t.value} value={t.value}>
                  {t.label}
                </option>
              ))}
            </Select>
          </Field>
          <Field label="简介 (short description)">
            <Input
              value={form.shortDescription ?? ""}
              onChange={(e) => set("shortDescription", e.target.value)}
              placeholder="一句话描述"
            />
          </Field>
          <Field label="详细介绍 (long description, Markdown)">
            <Textarea
              value={form.longDescription ?? ""}
              onChange={(e) => set("longDescription", e.target.value)}
              rows={8}
              placeholder="支持 Markdown"
            />
          </Field>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle className="text-base">许可证与链接</CardTitle>
        </CardHeader>
        <CardContent className="space-y-4">
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <Field label="代码许可证 (license)">
              <Input
                value={form.license ?? ""}
                onChange={(e) => set("license", e.target.value)}
                placeholder="MIT"
              />
            </Field>
            <Field label="素材许可证 (media license)">
              <Input
                value={form.mediaLicense ?? ""}
                onChange={(e) => set("mediaLicense", e.target.value)}
                placeholder="CC BY-SA 4.0"
              />
            </Field>
          </div>
          <Field label="源码仓库 (repo)">
            <Input
              value={form.repo ?? ""}
              onChange={(e) => set("repo", e.target.value)}
              placeholder="https://github.com/user/repo"
            />
          </Field>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <Field label="官网 (website)">
              <Input value={form.website ?? ""} onChange={(e) => set("website", e.target.value)} />
            </Field>
            <Field label="问题追踪 (issue tracker)">
              <Input
                value={form.issueTracker ?? ""}
                onChange={(e) => set("issueTracker", e.target.value)}
              />
            </Field>
          </div>
          <Field label="捐赠链接 (donate url)">
            <Input value={form.donateUrl ?? ""} onChange={(e) => set("donateUrl", e.target.value)} />
          </Field>
          <Field label="标签 (tags,逗号分隔)">
            <Input
              value={tagsText}
              onChange={(e) => setTagsText(e.target.value)}
              placeholder="building, decorative"
            />
          </Field>
        </CardContent>
      </Card>

      <div className="flex items-center justify-end gap-3">
        <Button type="button" variant="outline" onClick={() => router.back()} disabled={saving}>
          取消
        </Button>
        <Button type="submit" disabled={saving}>
          {saving ? <Loader2 className="animate-spin" /> : <Save />}
          {mode === "create" ? "创建" : "保存"}
        </Button>
      </div>
    </form>
  );
}

function Field({
  label,
  hint,
  children,
}: {
  label: string;
  hint?: string;
  children: React.ReactNode;
}) {
  return (
    <div className="space-y-1.5">
      <Label>{label}</Label>
      {children}
      {hint && <p className="text-xs text-muted-foreground">{hint}</p>}
    </div>
  );
}
