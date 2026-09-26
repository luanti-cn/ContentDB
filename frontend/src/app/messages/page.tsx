"use client";

// 网页聊天:左侧会话列表(好友 + 未读),右侧聊天窗。
// 发送走 REST;接收走 WS(dm.new)实时推送;进入会话自动标记已读。

import * as React from "react";
import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { api, type ChatMessage, type FriendInfo, type UnreadPeer } from "@/lib/api";
import { useRealtimeEvent, useRealtimeStatus, type RealtimeEvent } from "@/lib/realtime";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent } from "@/components/ui/card";
import { ArrowLeft, Loader2, MessageCircle, Send, UserRound } from "lucide-react";

function timeLabel(iso: string): string {
  const t = new Date(iso);
  const sameDay = new Date().toDateString() === t.toDateString();
  return sameDay
    ? t.toLocaleTimeString("zh-CN", { hour: "2-digit", minute: "2-digit" })
    : t.toLocaleString("zh-CN", {
        month: "2-digit",
        day: "2-digit",
        hour: "2-digit",
        minute: "2-digit",
      });
}

export default function MessagesPage() {
  // useSearchParams 需要 Suspense 边界(build 预渲染要求)
  return (
    <React.Suspense fallback={<div className="mx-auto h-96 max-w-5xl animate-pulse rounded-lg bg-muted" />}>
      <MessagesInner />
    </React.Suspense>
  );
}

function MessagesInner() {
  const params = useSearchParams();
  const router = useRouter();
  const active = params.get("to") ?? "";

  const [friends, setFriends] = React.useState<FriendInfo[] | null>(null);
  const [unread, setUnread] = React.useState<Record<string, number>>({});
  const [error, setError] = React.useState<string | null>(null);
  const wsOnline = useRealtimeStatus();

  const loadList = React.useCallback(() => {
    api.listFriends().then((r) => setFriends(r.friends)).catch(() => {});
    api
      .unreadMessages()
      .then((r) => {
        const map: Record<string, number> = {};
        r.unread.forEach((u: UnreadPeer) => (map[u.username] = u.count));
        setUnread(map);
      })
      .catch(() => {});
  }, []);

  React.useEffect(() => {
    loadList();
    // 在线状态 30 秒兜底刷新
    const timer = setInterval(() => {
      api.listFriends().then((r) => setFriends(r.friends)).catch(() => {});
    }, 30000);
    return () => clearInterval(timer);
  }, [loadList]);

  // presence 事件按 inGame 分流(与好友页一致)
  useRealtimeEvent((ev: RealtimeEvent) => {
    if (ev.type === "presence") {
      const username = String(ev.username ?? "");
      const online = ev.online === true;
      const inGame = ev.inGame === true;
      setFriends((prev) =>
        prev?.map((f) => {
          if (f.username !== username) return f;
          if (inGame) {
            return {
              ...f,
              online,
              currentServerAddress: (ev.address as string | null) ?? null,
              presenceAt: online ? new Date().toISOString() : f.presenceAt,
            };
          }
          return { ...f, siteOnline: online };
        }) ?? prev
      );
    } else if (ev.type === "friend.accepted") {
      loadList();
    }
  });

  const onEvent = React.useCallback(
    (ev: RealtimeEvent) => {
      if (ev.type === "dm.new") {
        const from = String(ev.from ?? "");
        if (from !== active) {
          setUnread((m) => ({ ...m, [from]: (m[from] ?? 0) + 1 }));
        }
      } else if (ev.type === "dm.read") {
        // 对方已读:无需改未读数
      } else if (ev.type === "friend.accepted") {
        loadList();
      }
    },
    [active, loadList]
  );
  useRealtimeEvent(onEvent);

  const totalUnread = Object.values(unread).reduce((a, b) => a + b, 0);

  // 稳定引用:值没变不触发渲染,也避免子组件 effect 因新函数引用无限重跑
  const handleRead = React.useCallback(
    (username: string) =>
      setUnread((m) => (m[username] ? { ...m, [username]: 0 } : m)),
    []
  );

  return (
    <div className="mx-auto max-w-5xl space-y-4">
      <div className="flex items-center gap-2">
        <MessageCircle className="size-5 text-primary" />
        <h1 className="text-2xl font-bold tracking-tight">消息</h1>
        {wsOnline ? (
          <Badge variant="outline" className="text-emerald-600">
            实时已连接
          </Badge>
        ) : (
          <Badge variant="outline" className="text-muted-foreground">
            实时未连接
          </Badge>
        )}
        {totalUnread > 0 && (
          <Badge className="bg-primary/15 text-primary">{totalUnread} 条未读</Badge>
        )}
      </div>

      {error && (
        <div className="rounded-lg border border-destructive/40 bg-destructive/5 p-4 text-sm text-destructive">
          {error}(可能未登录,请先{" "}
          <a href="/login" className="underline">
            登录
          </a>
          )
        </div>
      )}

      <div className="grid gap-4 md:grid-cols-[240px_1fr]">
        {/* 会话列表 */}
        <Card className="h-fit md:sticky md:top-20">
          <CardContent className="p-2">
            {friends === null ? (
              <div className="h-40 animate-pulse rounded-md bg-muted" />
            ) : friends.length === 0 ? (
              <p className="p-3 text-sm text-muted-foreground">
                还没有好友,先去
                <Link href="/friends" className="mx-1 underline">
                  好友页
                </Link>
                添加。
              </p>
            ) : (
              <div className="space-y-1">
                {friends.map((f) => (
                  <button
                    key={f.username}
                    onClick={() => {
                      router.push(`/messages?to=${encodeURIComponent(f.username)}`);
                      setUnread((m) => ({ ...m, [f.username]: 0 }));
                    }}
                    className={`flex w-full items-center gap-2.5 rounded-md px-2.5 py-2 text-left transition-colors hover:bg-accent ${
                      f.username === active ? "bg-accent" : ""
                    }`}
                  >
                    {f.profilePicUrl ? (
                      // eslint-disable-next-line @next/next/no-img-element
                      <img
                        src={f.profilePicUrl}
                        alt={f.username}
                        className="size-8 shrink-0 rounded-full border object-cover"
                      />
                    ) : (
                      <div className="flex size-8 shrink-0 items-center justify-center rounded-full border bg-muted">
                        <UserRound className="size-3.5 text-muted-foreground" />
                      </div>
                    )}
                    <span className="min-w-0 flex-1">
                      <span className="block truncate text-sm font-medium">
                        {f.displayName || f.username}
                      </span>
                      <span
                        className={`block text-xs ${
                          f.online
                            ? "text-emerald-600"
                            : f.siteOnline
                              ? "text-sky-600"
                              : "text-muted-foreground"
                        }`}
                      >
                        {f.online ? "游戏在线" : f.siteOnline ? "网站在线" : "离线"}
                      </span>
                    </span>
                    {(unread[f.username] ?? 0) > 0 && (
                      <span className="flex size-5 shrink-0 items-center justify-center rounded-full bg-primary text-[11px] font-semibold text-primary-foreground">
                        {unread[f.username]}
                      </span>
                    )}
                  </button>
                ))}
              </div>
            )}
          </CardContent>
        </Card>

        {/* 聊天窗 */}
        {active ? (
          <ChatWindow key={active} username={active} friends={friends} onRead={handleRead} />
        ) : (
          <Card>
            <CardContent className="flex h-80 items-center justify-center text-sm text-muted-foreground">
              从左侧选择一个会话
            </CardContent>
          </Card>
        )}
      </div>
    </div>
  );
}

function ChatWindow({
  username,
  friends,
  onRead,
}: {
  username: string;
  friends: FriendInfo[] | null;
  onRead: (username: string) => void;
}) {
  const friend = friends?.find((f) => f.username === username);
  const [messages, setMessages] = React.useState<ChatMessage[] | null>(null);
  const [hasMore, setHasMore] = React.useState(false);
  const [text, setText] = React.useState("");
  const [sending, setSending] = React.useState(false);
  const bottomRef = React.useRef<HTMLDivElement>(null);

  const scrollToBottom = React.useCallback(() => {
    requestAnimationFrame(() => bottomRef.current?.scrollIntoView({ behavior: "smooth" }));
  }, []);

  const load = React.useCallback(
    async (before?: number) => {
      try {
        const r = await api.listMessages(username, before);
        setMessages((prev) =>
          before ? [...(r.messages).reverse(), ...(prev ?? [])] : [...r.messages].reverse()
        );
        setHasMore(r.hasMore);
        if (!before) scrollToBottom();
      } catch {
        setMessages([]);
      }
    },
    [username, scrollToBottom]
  );

  // 回调用 ref 持有,effect 只随 username 变化执行一次,避免无限循环
  const loadRef = React.useRef(load);
  loadRef.current = load;
  const onReadRef = React.useRef(onRead);
  onReadRef.current = onRead;

  React.useEffect(() => {
    setMessages(null);
    loadRef.current();
    api.markMessagesRead(username).catch(() => {});
    onReadRef.current(username);
  }, [username]);

  const onEvent = React.useCallback(
    (ev: RealtimeEvent) => {
      if (ev.type === "dm.new" && String(ev.from ?? "") === username) {
        const msg: ChatMessage = {
          id: Number(ev.id ?? Date.now()),
          from: String(ev.from ?? ""),
          fromDisplay: ev.fromDisplay as string | null,
          to: "me",
          body: String(ev.body ?? ""),
          createdAt: String(ev.ts ?? new Date().toISOString()),
          readAt: null,
        };
        setMessages((prev) => (prev ? [...prev.filter((m) => m.id !== msg.id), msg] : [msg]));
        scrollToBottom();
        api.markMessagesRead(username).catch(() => {});
        onReadRef.current(username);
      }
    },
    [username, scrollToBottom]
  );
  useRealtimeEvent(onEvent);

  // 已读回执:对方读了我发的消息(我的消息 = from 不是对方的那些)
  useRealtimeEvent((ev: RealtimeEvent) => {
    if (ev.type === "dm.read" && String(ev.by ?? "") === username) {
      const ts = String(ev.ts ?? new Date().toISOString());
      setMessages(
        (prev) => prev?.map((m) => (m.from !== username && !m.readAt ? { ...m, readAt: ts } : m)) ?? prev
      );
    }
  });

  async function send(e: React.FormEvent) {
    e.preventDefault();
    const body = text.trim();
    if (!body || sending) return;
    setSending(true);
    try {
      const msg = await api.sendMessage(username, body);
      setMessages((prev) => [...(prev ?? []), msg]);
      setText("");
      scrollToBottom();
    } catch (err) {
      alert(err instanceof Error ? err.message : "发送失败");
    } finally {
      setSending(false);
    }
  }

  return (
    <Card className="flex min-h-[480px] flex-col">
      <CardContent className="flex min-h-0 flex-1 flex-col p-0">
        <div className="flex items-center gap-2 border-b px-4 py-3">
          <Link href="/messages" className="md:hidden">
            <ArrowLeft className="size-4" />
          </Link>
          <span className="text-sm font-semibold">{friend?.displayName || username}</span>
          <span className="text-xs text-muted-foreground">@{username}</span>
          {friend && (
            <span
              className={`ml-auto text-xs font-medium ${
                friend.online ? "text-emerald-600" : friend.siteOnline ? "text-sky-600" : "text-muted-foreground"
              }`}
            >
              {friend.online
                ? `游戏在线${friend.currentServerAddress ? ` · ${friend.currentServerAddress}` : ""}`
                : friend.siteOnline
                  ? "网站在线"
                  : "离线"}
            </span>
          )}
        </div>

        <div className="min-h-0 flex-1 space-y-2 overflow-y-auto p-4">
          {messages === null ? (
            <div className="flex h-full items-center justify-center">
              <Loader2 className="animate-spin text-muted-foreground" />
            </div>
          ) : (
            <>
              {hasMore && (
                <div className="text-center">
                  <Button
                    variant="ghost"
                    size="sm"
                    onClick={() => messages.length > 0 && load(messages[0].id)}
                  >
                    加载更早的消息
                  </Button>
                </div>
              )}
              {messages.length === 0 && (
                <p className="pt-10 text-center text-sm text-muted-foreground">
                  还没有消息,打个招呼吧。
                </p>
              )}
              {messages.map((m) => {
                const mine = m.from !== username;
                return (
                  <div
                    key={m.id}
                    className={`flex ${mine ? "justify-end" : "justify-start"}`}
                  >
                    <div
                      className={`max-w-[75%] rounded-2xl px-3.5 py-2 text-sm ${
                        mine
                          ? "rounded-br-sm bg-primary text-primary-foreground"
                          : "rounded-bl-sm bg-muted"
                      }`}
                    >
                      <p className="whitespace-pre-wrap break-words">{m.body}</p>
                      <p
                        className={`mt-0.5 text-right text-[10px] ${
                          mine ? "text-primary-foreground/70" : "text-muted-foreground"
                        }`}
                      >
                        {timeLabel(m.createdAt)}
                        {mine && (
                          <span className="ml-1">{m.readAt ? "已读" : "已发送"}</span>
                        )}
                      </p>
                    </div>
                  </div>
                );
              })}
              <div ref={bottomRef} />
            </>
          )}
        </div>

        <form onSubmit={send} className="flex items-center gap-2 border-t p-3">
          <Input
            value={text}
            onChange={(e) => setText(e.target.value)}
            placeholder={`发消息给 ${username}…`}
            maxLength={2000}
          />
          <Button type="submit" size="icon" disabled={sending || !text.trim()}>
            {sending ? <Loader2 className="animate-spin" /> : <Send />}
          </Button>
        </form>
      </CardContent>
    </Card>
  );
}
