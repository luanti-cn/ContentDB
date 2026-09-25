"use client";

import * as React from "react";
import { api, type CharacterInfo, type CharacterPasswordType } from "@/lib/api";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select } from "@/components/ui/select";
import { Textarea } from "@/components/ui/textarea";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { useToast } from "@/components/ui/toast";
import {
  Download,
  Eye,
  ImageUp,
  Loader2,
  Plus,
  Trash2,
  UserRound,
  Pencil,
} from "lucide-react";

const PASSWORD_TYPE_LABEL: Record<CharacterPasswordType, string> = {
  FIXED: "固定密码",
  LIST_ROTATE: "列表循环",
  RANDOM: "随机生成",
};

export function CharactersTab() {
  const { toast } = useToast();
  const [characters, setCharacters] = React.useState<CharacterInfo[] | null>(null);
  const [error, setError] = React.useState<string | null>(null);
  const [editing, setEditing] = React.useState<CharacterInfo | null>(null);

  const load = React.useCallback(() => {
    api
      .listCharacters()
      .then((r) => {
        setCharacters(r.characters);
        setError(null);
      })
      .catch((e) => setError(e instanceof Error ? e.message : "加载失败"));
  }, []);

  React.useEffect(() => {
    load();
  }, [load]);

  function onSaved() {
    setEditing(null);
    load();
  }

  async function remove(id: number) {
    if (!confirm("删除该角色?已配给到各服务器的凭证不受影响。")) return;
    try {
      await api.deleteCharacter(id);
      toast("已删除");
      load();
    } catch (err) {
      toast(err instanceof Error ? err.message : "删除失败", "error");
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
          <CardTitle className="text-base">
            {editing ? `编辑角色「${editing.name}」` : "创建新角色"}
          </CardTitle>
        </CardHeader>
        <CardContent>
          <CharacterForm
            key={editing?.id ?? "new"}
            initial={editing}
            onCancel={editing ? () => setEditing(null) : undefined}
            onSaved={onSaved}
          />
        </CardContent>
      </Card>

      {characters === null && !error ? (
        <div className="h-24 animate-pulse rounded-md bg-muted" />
      ) : characters && characters.length === 0 ? (
        <p className="text-sm text-muted-foreground">还没有角色。创建一个,进新服务器时即可选用。</p>
      ) : characters ? (
        <div className="grid gap-4 sm:grid-cols-2">
          {characters.map((c) => (
            <CharacterCard
              key={c.id}
              character={c}
              onEdit={() => setEditing(c)}
              onDelete={() => remove(c.id)}
              onChanged={load}
            />
          ))}
        </div>
      ) : null}
    </div>
  );
}

function CharacterForm({
  initial,
  onSaved,
  onCancel,
}: {
  initial?: CharacterInfo | null;
  onSaved: () => void;
  onCancel?: () => void;
}) {
  const { toast } = useToast();
  const [name, setName] = React.useState(initial?.name ?? "");
  const [passwordType, setPasswordType] = React.useState<CharacterPasswordType>(
    initial?.passwordType ?? "RANDOM"
  );
  const [password, setPassword] = React.useState("");
  const [passwordList, setPasswordList] = React.useState("");
  const [busy, setBusy] = React.useState(false);

  const editing = Boolean(initial);

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    if (!name.trim()) {
      toast("请填写角色名(即游戏内用户名)", "error");
      return;
    }
    if (passwordType === "FIXED" && !editing && !password) {
      toast("固定密码类型需要填写密码", "error");
      return;
    }
    if (passwordType === "LIST_ROTATE" && !editing && !passwordList.trim()) {
      toast("列表循环类型需要至少一条密码", "error");
      return;
    }
    setBusy(true);
    try {
      if (editing && initial) {
        await api.updateCharacter(initial.id, {
          name: name.trim(),
          passwordType,
          ...(password ? { password } : {}),
          ...(passwordList.trim()
            ? { passwordList: passwordList.split("\n").map((s) => s.trim()).filter(Boolean) }
            : {}),
        });
        toast("已保存");
      } else {
        await api.createCharacter({
          name: name.trim(),
          passwordType,
          ...(password ? { password } : {}),
          ...(passwordList.trim()
            ? { passwordList: passwordList.split("\n").map((s) => s.trim()).filter(Boolean) }
            : {}),
        });
        toast("角色已创建");
      }
      setPassword("");
      setPasswordList("");
      onSaved();
    } catch (err) {
      toast(err instanceof Error ? err.message : "保存失败", "error");
    } finally {
      setBusy(false);
    }
  }

  return (
    <form onSubmit={submit} className="space-y-4">
      <div className="grid gap-4 sm:grid-cols-2">
        <div className="space-y-1.5">
          <Label>角色名(游戏内用户名)</Label>
          <Input
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder="Steve(1-20 位字母数字下划线连字符)"
          />
        </div>
        <div className="space-y-1.5">
          <Label>密码类型</Label>
          <Select
            value={passwordType}
            onChange={(e) => setPasswordType(e.target.value as CharacterPasswordType)}
          >
            <option value="RANDOM">随机生成(每个新服务器随机)</option>
            <option value="FIXED">固定密码(所有新服务器共用)</option>
            <option value="LIST_ROTATE">列表循环(依次取用)</option>
          </Select>
        </div>
      </div>

      {passwordType === "FIXED" && (
        <div className="space-y-1.5">
          <Label>{editing ? "新密码(留空不改)" : "固定密码"}</Label>
          <Input
            type="text"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            placeholder="用于所有新服务器的密码"
          />
        </div>
      )}
      {passwordType === "LIST_ROTATE" && (
        <div className="space-y-1.5">
          <Label>{editing ? "新密码列表(每行一条,留空不改)" : "密码列表(每行一条)"}</Label>
          <Textarea
            rows={3}
            value={passwordList}
            onChange={(e) => setPasswordList(e.target.value)}
            placeholder={"password1\npassword2\npassword3"}
          />
          <p className="text-xs text-muted-foreground">
            每配给一个新服务器按顺序取一条,用完从头循环。
          </p>
        </div>
      )}

      {!editing && (
        <p className="text-xs text-muted-foreground">
          皮肤支持 Luanti 64x32 与 Minecraft 64x64(上传后自动转换为 Luanti 格式);角色卡片可随时「导出MC」转回 64x64。
        </p>
      )}

      <div className="flex gap-2">
        <Button type="submit" disabled={busy}>
          {busy ? <Loader2 className="animate-spin" /> : <Plus />} {editing ? "保存" : "创建"}
        </Button>
        {onCancel && (
          <Button type="button" variant="ghost" onClick={onCancel}>
            取消
          </Button>
        )}
      </div>
    </form>
  );
}

// 从 Luanti 皮肤图裁出头部正面(64x32/64x64 布局中位于 (8,8) 的 8x8 区域)作为头像
function SkinAvatar({ src, name, size = 80 }: { src: string; name: string; size?: number }) {
  return (
    <div
      role="img"
      aria-label={`${name} 的皮肤头像`}
      title={name}
      className="shrink-0 rounded-md border bg-muted"
      style={{
        width: size,
        height: size,
        backgroundImage: `url(${src})`,
        // 横向 8 倍(64px 宽),纵向 auto 保持比例,兼容 64x32(Luanti)与 64x64(MC)皮肤
        backgroundSize: `${size * 8}px auto`,
        backgroundPosition: `-${size}px -${size}px`,
        imageRendering: "pixelated",
        backgroundRepeat: "no-repeat",
      }}
    />
  );
}

function CharacterCard({
  character,
  onEdit,
  onDelete,
  onChanged,
}: {
  character: CharacterInfo;
  onEdit: () => void;
  onDelete: () => void;
  onChanged: () => void;
}) {
  const { toast } = useToast();
  const [revealed, setRevealed] = React.useState<string | null>(null);
  const [uploading, setUploading] = React.useState(false);
  const fileRef = React.useRef<HTMLInputElement>(null);

  async function reveal() {
    if (revealed !== null) {
      setRevealed(null);
      return;
    }
    try {
      const res = await api.revealCharacter(character.id);
      if (res.password) {
        setRevealed(res.password);
      } else if (res.passwordType === "LIST_ROTATE") {
        setRevealed(`下一条(${(res.rotateIndex ?? 0) + 1}/${res.passwordListCount ?? 0})`);
      } else {
        setRevealed("(随机生成,进服时才产生)");
      }
    } catch (err) {
      toast(err instanceof Error ? err.message : "查看失败", "error");
    }
  }

  async function uploadSkin(file: File) {
    setUploading(true);
    try {
      const form = new FormData();
      form.append("file", file);
      await api.uploadCharacterSkin(character.id, form);
      toast("皮肤已更新");
      onChanged();
    } catch (err) {
      toast(err instanceof Error ? err.message : "上传失败", "error");
    } finally {
      setUploading(false);
    }
  }

  return (
    <Card className="overflow-hidden">
      <CardContent className="flex gap-4 p-4">
        {character.skinUrl ? (
          <SkinAvatar src={character.skinUrl} name={character.name} />
        ) : (
          <div className="flex size-20 shrink-0 items-center justify-center rounded-md border bg-muted">
            <UserRound className="size-8 text-muted-foreground" />
          </div>
        )}
        <div className="min-w-0 flex-1 space-y-2">
          <div className="flex items-center gap-2">
            <span className="truncate font-semibold">{character.name}</span>
            <Badge variant="secondary">{PASSWORD_TYPE_LABEL[character.passwordType]}</Badge>
          </div>

          {revealed !== null && (
            <code className="block truncate rounded border bg-muted px-2 py-1 text-xs">
              {revealed}
            </code>
          )}

          <div className="flex flex-wrap gap-1">
            <Button size="sm" variant="ghost" onClick={reveal}>
              <Eye /> 查看密码
            </Button>
            <Button
              size="sm"
              variant="ghost"
              disabled={uploading}
              onClick={() => fileRef.current?.click()}
            >
              {uploading ? <Loader2 className="animate-spin" /> : <ImageUp />} 上传皮肤
            </Button>
            <Button size="sm" variant="ghost" onClick={onEdit}>
              <Pencil /> 编辑
            </Button>
            <Button asChild size="sm" variant="ghost" title="下载为 Minecraft 64x64 皮肤">
              <a href={api.characterMinecraftSkinUrl(character.id)} download>
                <Download /> 导出MC
              </a>
            </Button>
            <Button size="sm" variant="ghost" onClick={onDelete}>
              <Trash2 />
            </Button>
            <input
              ref={fileRef}
              type="file"
              accept=".png,.jpg,.jpeg,.webp"
              className="hidden"
              onChange={(e) => {
                const f = e.target.files?.[0];
                if (f) uploadSkin(f);
                e.target.value = "";
              }}
            />
          </div>
        </div>
      </CardContent>
    </Card>
  );
}
