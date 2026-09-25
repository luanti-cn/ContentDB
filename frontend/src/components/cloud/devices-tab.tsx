"use client";

import * as React from "react";
import { QRCodeSVG } from "qrcode.react";
import { api, type DeviceInfo, type PairingCreated, type PairingStatusInfo } from "@/lib/api";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
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
import { Badge } from "@/components/ui/badge";
import { useToast } from "@/components/ui/toast";
import { Check, Copy, Loader2, Plus, Smartphone, Trash2 } from "lucide-react";

export function DevicesTab() {
  const { toast } = useToast();
  const [devices, setDevices] = React.useState<DeviceInfo[] | null>(null);
  const [error, setError] = React.useState<string | null>(null);
  const [pairing, setPairing] = React.useState<PairingCreated | null>(null);
  const [deviceName, setDeviceName] = React.useState("");
  const [busy, setBusy] = React.useState(false);

  const load = React.useCallback(() => {
    api
      .listDevices()
      .then((r) => {
        setDevices(r.devices);
        setError(null);
      })
      .catch((e) => setError(e instanceof Error ? e.message : "加载失败"));
  }, []);

  React.useEffect(() => {
    load();
  }, [load]);

  async function startPairing(e: React.FormEvent) {
    e.preventDefault();
    if (!deviceName.trim()) {
      toast("请填写设备名称", "error");
      return;
    }
    setBusy(true);
    try {
      const res = await api.createPairing(deviceName.trim());
      setPairing(res);
      setDeviceName("");
    } catch (err) {
      toast(err instanceof Error ? err.message : "创建配对失败", "error");
    } finally {
      setBusy(false);
    }
  }

  async function revoke(id: number) {
    if (!confirm("吊销该设备?其客户端将立即失去访问权限。")) return;
    try {
      await api.revokeDevice(id);
      toast("已吊销");
      load();
    } catch (err) {
      toast(err instanceof Error ? err.message : "吊销失败", "error");
    }
  }

  return (
    <div className="space-y-6">
      {error && (
        <div className="rounded-lg border border-destructive/40 bg-destructive/5 p-4 text-sm text-destructive">
          {error}(可能未登录,请先{" "}
          <a href="/login" className="underline">
            登录
          </a>
          )
        </div>
      )}

      <Card>
        <CardHeader>
          <CardTitle className="text-base">配对新设备</CardTitle>
        </CardHeader>
        <CardContent className="space-y-4">
          <form onSubmit={startPairing} className="flex items-end gap-3">
            <div className="flex-1 space-y-1.5">
              <Label>设备名称</Label>
              <Input
                value={deviceName}
                onChange={(e) => setDeviceName(e.target.value)}
                placeholder="我的电脑 / 手机模拟器"
              />
            </div>
            <Button type="submit" disabled={busy}>
              {busy ? <Loader2 className="animate-spin" /> : <Plus />} 生成配对码
            </Button>
          </form>

          {pairing && (
            <PairingPanel
              pairing={pairing}
              onDone={() => {
                setPairing(null);
                load();
              }}
            />
          )}
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle className="text-base">已配对设备</CardTitle>
        </CardHeader>
        <CardContent>
          {devices === null && !error ? (
            <div className="h-16 animate-pulse rounded-md bg-muted" />
          ) : devices && devices.length === 0 ? (
            <p className="text-sm text-muted-foreground">还没有配对设备。</p>
          ) : devices ? (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>设备</TableHead>
                  <TableHead>Token</TableHead>
                  <TableHead>最近使用</TableHead>
                  <TableHead className="text-right">操作</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {devices.map((d) => (
                  <TableRow key={d.id}>
                    <TableCell className="font-medium">
                      <span className="flex items-center gap-2">
                        <Smartphone className="size-4 text-muted-foreground" />
                        {d.name}
                      </span>
                    </TableCell>
                    <TableCell>
                      <code className="text-xs text-muted-foreground">{d.tokenPrefix}…</code>
                    </TableCell>
                    <TableCell>
                      {d.lastUsedAt ? new Date(d.lastUsedAt).toLocaleString("zh-CN") : "从未"}
                    </TableCell>
                    <TableCell className="text-right">
                      <Button variant="ghost" size="sm" onClick={() => revoke(d.id)}>
                        <Trash2 />
                      </Button>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          ) : null}
        </CardContent>
      </Card>
    </div>
  );
}

function PairingPanel({
  pairing,
  onDone,
}: {
  pairing: PairingCreated;
  onDone: () => void;
}) {
  const { toast } = useToast();
  const [status, setStatus] = React.useState<PairingStatusInfo["status"]>("pending");
  const [copied, setCopied] = React.useState(false);

  React.useEffect(() => {
    if (status !== "pending") return;
    const timer = setInterval(async () => {
      try {
        const res = await api.pairingStatus(pairing.id);
        setStatus(res.status);
        if (res.status === "claimed") {
          toast("设备配对成功");
          clearInterval(timer);
          setTimeout(onDone, 1200);
        }
        if (res.status === "expired") clearInterval(timer);
      } catch {
        /* 轮询失败忽略,下轮重试 */
      }
    }, 2000);
    return () => clearInterval(timer);
  }, [pairing.id, status, toast, onDone]);

  async function copyCode() {
    try {
      await navigator.clipboard.writeText(pairing.code);
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    } catch {
      /* ignore */
    }
  }

  return (
    <div className="flex flex-col items-center gap-4 rounded-lg border border-primary/40 bg-primary/5 p-6 sm:flex-row sm:items-start">
      <div className="rounded-lg border bg-background p-2">
        <QRCodeSVG value={pairing.deepLink} size={140} />
      </div>
      <div className="flex-1 space-y-3 text-center sm:text-left">
        <p className="text-sm font-medium">
          用 Luanti 客户端扫码,或手动输入配对码(10 分钟内有效):
        </p>
        <div className="flex items-center justify-center gap-2 sm:justify-start">
          <span className="rounded-md border bg-background px-4 py-2 font-mono text-2xl font-bold tracking-[0.3em]">
            {pairing.code}
          </span>
          <Button size="sm" variant="outline" onClick={copyCode}>
            {copied ? <Check /> : <Copy />} {copied ? "已复制" : "复制"}
          </Button>
        </div>
        <p className="flex items-center gap-2 text-xs text-muted-foreground">
          {status === "pending" && (
            <>
              <Loader2 className="size-3 animate-spin" /> 等待客户端配对…
            </>
          )}
          {status === "claimed" && (
            <Badge className="bg-primary/15 text-primary">
              <Check className="mr-1 size-3" /> 配对成功
            </Badge>
          )}
          {status === "expired" && <Badge variant="destructive">配对码已过期,请重新生成</Badge>}
        </p>
      </div>
    </div>
  );
}
