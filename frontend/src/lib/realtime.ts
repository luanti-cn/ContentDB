"use client";

// 实时通讯 WS 客户端(网页端,cookie 会话认证)
// - 单例连接,自动重连(指数退避),所有页面共享
// - 事件订阅:dm.new / dm.read / presence / friend.request / friend.accepted / party.update
// - 心跳:30s ping;服务端 90s 无帧判死

import * as React from "react";

export interface RealtimeEvent {
  type: string;
  [key: string]: unknown;
}

type Handler = (ev: RealtimeEvent) => void;

function resolveWsUrl(): string {
  const configured = process.env.NEXT_PUBLIC_WS_URL;
  if (configured) {
    // 允许只配 origin(如 ws://localhost:5175),自动补全端点路径
    const endpoint = "/api/cloud/client/ws/";
    const base = configured.replace(/\/+$/, "");
    return base.endsWith(endpoint) ? base : base + endpoint;
  }
  if (typeof window === "undefined") return "";
  const proto = window.location.protocol === "https:" ? "wss:" : "ws:";
  return `${proto}//${window.location.host}/api/cloud/client/ws/`;
}

class RealtimeClient {
  private ws: WebSocket | null = null;
  private handlers = new Set<Handler>();
  private statusHandlers = new Set<(online: boolean) => void>();
  private reconnectTimer: ReturnType<typeof setTimeout> | null = null;
  private pingTimer: ReturnType<typeof setInterval> | null = null;
  private attempt = 0;
  private closedByUser = false;
  private started = false;

  get connected(): boolean {
    return this.ws?.readyState === WebSocket.OPEN;
  }

  start() {
    if (this.started || typeof window === "undefined") return;
    this.started = true;
    this.closedByUser = false;
    this.connect();
  }

  stop() {
    this.closedByUser = true;
    this.cleanup();
  }

  on(handler: Handler): () => void {
    this.handlers.add(handler);
    this.start();
    return () => {
      this.handlers.delete(handler);
    };
  }

  onStatus(handler: (online: boolean) => void): () => void {
    this.statusHandlers.add(handler);
    handler(this.connected);
    this.start();
    return () => {
      this.statusHandlers.delete(handler);
    };
  }

  send(payload: Record<string, unknown>) {
    if (this.connected) this.ws!.send(JSON.stringify(payload));
  }

  private connect() {
    const url = resolveWsUrl();
    if (!url) return;

    // 跨域/CF 部署下 WS 握手带不上 cookie:先取一次性票据,以 ?ticket= 认证
    import("@/lib/api")
      .then((m) => m.api.wsTicket())
      .then((r) => {
        if (this.closedByUser) return;
        const u = new URL(url);
        u.searchParams.set("ticket", r.ticket);
        this.open(u.toString());
      })
      .catch(() => {
        // 未登录/后端不可达:退避后重试
        this.scheduleReconnect();
      });
  }

  private open(url: string) {
    let ws: WebSocket;
    try {
      ws = new WebSocket(url);
    } catch {
      this.scheduleReconnect();
      return;
    }
    this.ws = ws;

    ws.onopen = () => {
      this.attempt = 0;
      this.statusHandlers.forEach((h) => h(true));
      this.pingTimer = setInterval(() => this.send({ type: "ping" }), 30000);
    };

    ws.onmessage = (ev) => {
      let data: RealtimeEvent;
      try {
        data = JSON.parse(ev.data as string);
      } catch {
        return;
      }
      if (data.type === "pong") return;
      this.handlers.forEach((h) => {
        try {
          h(data);
        } catch (e) {
          console.error("[realtime] handler error", e);
        }
      });
    };

    ws.onclose = () => {
      this.statusHandlers.forEach((h) => h(false));
      if (!this.closedByUser) this.scheduleReconnect();
    };

    ws.onerror = () => ws.close();
  }

  private scheduleReconnect() {
    this.cleanup();
    const delay = Math.min(1000 * 2 ** this.attempt, 60000);
    this.attempt += 1;
    this.reconnectTimer = setTimeout(() => this.connect(), delay);
  }

  private cleanup() {
    if (this.reconnectTimer) clearTimeout(this.reconnectTimer);
    if (this.pingTimer) clearInterval(this.pingTimer);
    this.reconnectTimer = null;
    this.pingTimer = null;
    if (this.ws) {
      const ws = this.ws;
      this.ws = null;
      ws.onclose = null;
      ws.onerror = null;
      ws.onmessage = null;
      try {
        ws.close();
      } catch {
        /* already closing */
      }
    }
  }
}

export const realtime = new RealtimeClient();

/** 订阅实时事件(组件卸载自动退订)。 */
export function useRealtimeEvent(handler: Handler) {
  const ref = React.useRef(handler);
  ref.current = handler;
  React.useEffect(() => realtime.on((ev) => ref.current(ev)), []);
}

/** 订阅连接状态。 */
export function useRealtimeStatus(): boolean {
  const [online, setOnline] = React.useState(false);
  React.useEffect(() => realtime.onStatus(setOnline), []);
  return online;
}
