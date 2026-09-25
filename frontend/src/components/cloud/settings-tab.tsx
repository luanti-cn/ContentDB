"use client";

import * as React from "react";
import { api } from "@/lib/api";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { useToast } from "@/components/ui/toast";
import { Loader2 } from "lucide-react";

export function SettingsTab() {
  const { toast } = useToast();
  const [value, setValue] = React.useState<string | null>(null);
  const [error, setError] = React.useState<string | null>(null);
  const [busy, setBusy] = React.useState(false);

  React.useEffect(() => {
    api
      .getCloudSettings()
      .then((r) => {
        setValue(r.default_server_username);
        setError(null);
      })
      .catch((e) => setError(e instanceof Error ? e.message : "加载失败"));
  }, []);

  async function save(e: React.FormEvent) {
    e.preventDefault();
    setBusy(true);
    try {
      const res = await api.updateCloudSettings(value?.trim() ?? "");
      setValue(res.default_server_username);
      toast("已保存");
    } catch (err) {
      toast(err instanceof Error ? err.message : "保存失败", "error");
    } finally {
      setBusy(false);
    }
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">默认游戏内用户名</CardTitle>
      </CardHeader>
      <CardContent>
        {error && <p className="mb-3 text-sm text-destructive">{error}</p>}
        <form onSubmit={save} className="flex items-end gap-3">
          <div className="flex-1 space-y-1.5">
            <Label>未指定角色时,进新服务器使用的用户名</Label>
            <Input
              value={value ?? ""}
              onChange={(e) => setValue(e.target.value)}
              placeholder="留空则使用站点用户名"
              disabled={value === null}
            />
          </div>
          <Button type="submit" disabled={busy || value === null}>
            {busy ? <Loader2 className="animate-spin" /> : null} 保存
          </Button>
        </form>
        <p className="mt-3 text-xs text-muted-foreground">
          若该用户名在目标服务器已被占用,客户端会提示你在进服时改用其他名字。
        </p>
      </CardContent>
    </Card>
  );
}
