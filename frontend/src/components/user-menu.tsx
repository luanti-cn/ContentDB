"use client";

import * as React from "react";
import Link from "next/link";
import { Button } from "@/components/ui/button";
import { api } from "@/lib/api";
import {
  LogIn,
  User as UserIcon,
  Plus,
  KeyRound,
  Bell,
  ShieldAlert,
  ChevronDown,
  LogOut,
  Cloud,
  Users,
  Server,
} from "lucide-react";

/** 顶栏用户区:未登录显示登录按钮;已登录显示用户名下拉菜单。走后端 OIDC 会话。 */
export function UserMenu() {
  const [state, setState] = React.useState<
    | { loading: true }
    | { loading: false; username: string | null; rank?: string }
  >({ loading: true });
  const [open, setOpen] = React.useState(false);
  const ref = React.useRef<HTMLDivElement>(null);

  React.useEffect(() => {
    let alive = true;
    api
      .whoami()
      .then(
        (r) =>
          alive &&
          setState({
            loading: false,
            username: r.is_authenticated ? r.username : null,
            rank: r.rank,
          })
      )
      .catch(() => alive && setState({ loading: false, username: null }));
    return () => {
      alive = false;
    };
  }, []);

  React.useEffect(() => {
    function onClick(e: MouseEvent) {
      if (ref.current && !ref.current.contains(e.target as Node)) setOpen(false);
    }
    document.addEventListener("mousedown", onClick);
    return () => document.removeEventListener("mousedown", onClick);
  }, []);

  if (state.loading) {
    return <div className="h-8 w-16 animate-pulse rounded-md bg-muted" />;
  }

  if (!state.username) {
    return (
      <Button asChild size="sm">
        {/* 后端 /login 发起外部 OIDC 登录 */}
        <a href="/login">
          <LogIn className="mr-1 size-4" /> 登录
        </a>
      </Button>
    );
  }

  const rank = (state.rank ?? "").toUpperCase();
  const isModerator = ["EDITOR", "MODERATOR", "ADMIN"].some((x) => rank.includes(x));

  return (
    <div className="relative" ref={ref}>
      <button
        type="button"
        onClick={() => setOpen((o) => !o)}
        className="flex items-center gap-1.5 rounded-md px-2 py-1.5 text-sm hover:bg-accent"
      >
        <UserIcon className="size-4" />
        <span className="max-w-[8rem] truncate">{state.username}</span>
        <ChevronDown className="size-3.5 opacity-60" />
      </button>

      {open && (
        <div className="absolute right-0 mt-1 w-48 rounded-md border bg-popover p-1 shadow-lg">
          <MenuLink href={`/users/${state.username}`} icon={<UserIcon className="size-4" />}>
            我的主页
          </MenuLink>
          <MenuLink href="/packages/new" icon={<Plus className="size-4" />}>
            创建新包
          </MenuLink>
          <MenuLink href="/notifications" icon={<Bell className="size-4" />}>
            通知
          </MenuLink>
          <MenuLink href="/tokens" icon={<KeyRound className="size-4" />}>
            API Token
          </MenuLink>
          <MenuLink href="/friends" icon={<Users className="size-4" />}>
            好友
          </MenuLink>
          <MenuLink href="/party" icon={<Users className="size-4" />}>
            组队联机
          </MenuLink>
          <MenuLink href="/servers" icon={<Server className="size-4" />}>
            服务器大厅
          </MenuLink>
          <MenuLink href="/cloud" icon={<Cloud className="size-4" />}>
            云同步
          </MenuLink>
          {isModerator && (
            <MenuLink href="/admin" icon={<ShieldAlert className="size-4" />}>
              管理后台
            </MenuLink>
          )}
          <div className="my-1 border-t" />
          <a
            href="/logout"
            className="flex items-center gap-2 rounded-sm px-2 py-1.5 text-sm hover:bg-accent"
          >
            <LogOut className="size-4" /> 登出
          </a>
        </div>
      )}
    </div>
  );
}

function MenuLink({
  href,
  icon,
  children,
}: {
  href: string;
  icon: React.ReactNode;
  children: React.ReactNode;
}) {
  return (
    <Link
      href={href}
      className="flex items-center gap-2 rounded-sm px-2 py-1.5 text-sm hover:bg-accent"
    >
      {icon}
      {children}
    </Link>
  );
}
