// ContentDB 镜像 API 客户端
// 浏览器端:默认走同源(由 next.config rewrites 代理到 C# 后端);
// 服务端(SSR):Node 的 fetch 不接受相对 URL,必须用绝对地址,
//   优先 NEXT_PUBLIC_API_BASE,其次 BACKEND_ORIGIN(与 next.config 反代目标一致)。
// 也可通过 NEXT_PUBLIC_API_BASE 显式指向绝对地址(浏览器+服务端通用)。

/** 浏览器端使用的 base(可为空 = 同源相对路径,交给 rewrites 代理)。 */
export const API_BASE = process.env.NEXT_PUBLIC_API_BASE ?? "";

/** 解析出实际请求用的 base:服务端必须绝对,浏览器端可相对。 */
function resolveBase(): string {
  // 显式配置优先(两端通用)
  if (process.env.NEXT_PUBLIC_API_BASE) return process.env.NEXT_PUBLIC_API_BASE;

  // 浏览器端:相对路径即可(走同源 + rewrites)
  if (typeof window !== "undefined") return "";

  // 服务端(SSR):Node fetch 需要绝对 URL,回退到反代目标
  return process.env.BACKEND_ORIGIN ?? "http://localhost:5175";
}

export type ContentOrigin = "local" | "upstream";

export interface PackageShort {
  name: string;
  title: string;
  author: string;
  short_description?: string | null;
  type: string; // mod / game / txp
  release?: number | null;
  thumbnail?: string | null;
  aliases?: string[];
  repo?: string;
  featured?: boolean;
  /** 本站扩展:内容来源(本地自营 / 上游官方镜像) */
  origin?: ContentOrigin;
  /** 本站扩展:来源站点 id(如 local / luanti-org) */
  source?: string;
  /** 本站扩展:来源站点显示名(徽章) */
  source_name?: string | null;
  /** 本站扩展:创建时间(跨源排序用) */
  created_at?: string;
}

export interface ReleaseInfo {
  id: number;
  name: string;
  title: string;
  release_notes?: string | null;
  url?: string | null;
  release_date: string;
  commit?: string | null;
  downloads: number;
  size: number;
}

export interface PackageDetail extends PackageShort {
  long_description?: string | null;
  license?: string;
  media_license?: string;
  website?: string | null;
  issue_tracker?: string | null;
  forums?: number | null;
  forum_url?: string | null;
  donate_url?: string | null;
  tags?: string[];
  content_warnings?: string[];
  provides?: string[];
  screenshots?: string[];
  score?: number;
  downloads?: number;
}

async function getJson<T>(path: string, init?: RequestInit): Promise<T> {
  const res = await fetch(`${resolveBase()}${path}`, {
    // 列表/详情走服务端渲染时可缓存,这里保守用 no-store,交由页面自行覆盖
    cache: "no-store",
    headers: { Accept: "application/json" },
    ...init,
  });
  if (!res.ok) {
    throw new Error(`API ${path} 请求失败: ${res.status}`);
  }
  return (await res.json()) as T;
}

export interface PackageQuery {
  type?: string;
  q?: string;
  author?: string;
  tag?: string[];
  limit?: number;
  sort?: string;
  order?: "asc" | "desc";
}

function buildQuery(params: Record<string, unknown>): string {
  const sp = new URLSearchParams();
  for (const [k, v] of Object.entries(params)) {
    if (v === undefined || v === null || v === "") continue;
    if (Array.isArray(v)) v.forEach((x) => sp.append(k, String(x)));
    else sp.append(k, String(v));
  }
  const s = sp.toString();
  return s ? `?${s}` : "";
}

export const api = {
  listPackages: (query: PackageQuery = {}) =>
    getJson<PackageShort[]>(`/api/packages/${buildQuery(query as Record<string, unknown>)}`),

  getPackage: (author: string, name: string) =>
    getJson<PackageDetail>(`/api/packages/${author}/${name}/`),

  listReleases: (author: string, name: string) =>
    getJson<ReleaseInfo[]>(`/api/packages/${author}/${name}/releases/`),

  /** 下载端点(镜像会代理字节流或 302) */
  downloadUrl: (author: string, name: string, releaseId: number, reason = "new") =>
    `${API_BASE}/packages/${author}/${name}/releases/${releaseId}/download/?reason=${reason}`,

  /** 临时链接端点(返回预签名 URL 的 JSON) */
  tempLink: (author: string, name: string, releaseId: number) =>
    getJson<{ url: string; expires_in: number | null }>(
      `${API_BASE}/packages/${author}/${name}/releases/${releaseId}/temp-link/?format=json`
    ),

  /** 当前身份(浏览器端调用,带 cookie 会话)。 */
  whoami: () =>
    getJson<{ is_authenticated: boolean; username: string | null; rank?: string }>(
      "/api/whoami/",
      { credentials: "include" }
    ),

  /** 通知列表(需登录会话)。 */
  notifications: () =>
    getJson<NotificationItem[]>("/api/notifications/", { credentials: "include" }),

  markNotificationRead: (id: number) =>
    sendJson(`/api/notifications/${id}/read/`, "POST"),

  markAllNotificationsRead: () =>
    sendJson("/api/notifications/read-all/", "POST"),

  // ---- 包写操作(需会话或 API Token) ----

  createPackage: (body: PackageWriteBody) =>
    sendJson("/api/packages/", "POST", body),

  editPackage: (author: string, name: string, body: PackageWriteBody) =>
    sendJson(`/api/packages/${author}/${name}/`, "PUT", body),

  deletePackage: (author: string, name: string) =>
    sendJson(`/api/packages/${author}/${name}/`, "DELETE"),

  movePackageState: (author: string, name: string, state: string) =>
    sendJson(`/api/packages/${author}/${name}/state/`, "POST", { state }),

  // ---- 发布写操作 ----

  createGitRelease: (author: string, name: string, body: { ref: string; name?: string; title?: string; releaseNotes?: string }) =>
    sendJson(`/api/packages/${author}/${name}/releases/new/`, "POST", { method: "git", ...body }),

  uploadZipRelease: (author: string, name: string, form: FormData) =>
    sendForm(`/api/packages/${author}/${name}/releases/new/`, form),

  deleteRelease: (author: string, name: string, id: number) =>
    sendJson(`/api/packages/${author}/${name}/releases/${id}/`, "DELETE"),

  approveRelease: (author: string, name: string, id: number) =>
    sendJson(`/api/packages/${author}/${name}/releases/${id}/approve/`, "POST"),

  // ---- 截图写操作 ----

  listScreenshots: (author: string, name: string) =>
    getJson<ScreenshotInfo[]>(`/api/packages/${author}/${name}/screenshots/`),

  uploadScreenshot: (author: string, name: string, form: FormData) =>
    sendForm(`/api/packages/${author}/${name}/screenshots/new/`, form),

  deleteScreenshot: (author: string, name: string, id: number) =>
    sendJson(`/api/packages/${author}/${name}/screenshots/${id}/`, "DELETE"),

  orderScreenshots: (author: string, name: string, order: number[]) =>
    sendJson(`/api/packages/${author}/${name}/screenshots/order/`, "POST", order),

  setCoverImage: (author: string, name: string, coverImage: number) =>
    sendJson(`/api/packages/${author}/${name}/screenshots/cover-image/`, "POST", { coverImage }),

  // ---- 线程 / 评价 ----

  listThreads: (query: { author?: string; name?: string } = {}) =>
    getJson<ThreadSummary[]>(`/api/threads/${buildQuery(query as Record<string, unknown>)}`, {
      credentials: "include",
    }),

  getThread: (id: number) =>
    getJson<ThreadDetail>(`/api/threads/${id}/`, { credentials: "include" }),

  createThread: (body: { packageAuthor?: string; packageName?: string; title: string; comment: string; private?: boolean }) =>
    sendJson<{ id: number }>("/api/threads/new/", "POST", body),

  replyThread: (id: number, comment: string) =>
    sendJson(`/api/threads/${id}/reply/`, "POST", { comment }),

  listReviews: (author: string, name: string) =>
    getJson<ReviewInfo[]>(`/api/packages/${author}/${name}/reviews/`),

  submitReview: (author: string, name: string, body: { rating: number; title: string; comment: string; language?: string }) =>
    sendJson(`/api/packages/${author}/${name}/reviews/`, "POST", body),

  deleteReview: (author: string, name: string) =>
    sendJson(`/api/packages/${author}/${name}/reviews/`, "DELETE"),

  voteReview: (id: number, isPositive: boolean) =>
    sendJson(`/api/reviews/${id}/vote/`, "POST", { isPositive }),

  // ---- 合集 ----

  listCollections: () => getJson<CollectionInfo[]>("/api/collections/"),

  getCollection: (author: string, name: string) =>
    getJson<CollectionInfo>(`/api/collections/${author}/${name}/`),

  createCollection: (body: { name: string; title: string; shortDescription: string; longDescription?: string; private?: boolean }) =>
    sendJson("/api/collections/", "POST", body),

  editCollection: (author: string, name: string, body: { title?: string; shortDescription?: string; longDescription?: string; private?: boolean }) =>
    sendJson(`/api/collections/${author}/${name}/`, "PUT", body),

  deleteCollection: (author: string, name: string) =>
    sendJson(`/api/collections/${author}/${name}/`, "DELETE"),

  addToCollection: (author: string, name: string, body: { packageAuthor: string; packageName: string; description?: string }) =>
    sendJson(`/api/collections/${author}/${name}/add/`, "POST", body),

  removeFromCollection: (author: string, name: string, body: { packageAuthor: string; packageName: string }) =>
    sendJson(`/api/collections/${author}/${name}/remove/`, "POST", body),

  // ---- Token ----

  listTokens: () => getJson<TokenInfo[]>("/api/tokens/", { credentials: "include" }),

  createToken: (body: { name: string; packageAuthor?: string; packageName?: string }) =>
    sendJson<{ id: number; name: string; accessToken: string }>("/api/tokens/", "POST", body),

  deleteToken: (id: number) => sendJson(`/api/tokens/${id}/`, "DELETE"),

  // ---- 用户 ----

  getUser: (username: string) =>
    getJson<UserProfile>(`/api/users/${username}/`),

  // ---- 云同步:设备 / 角色 / 服务器凭证 ----

  listDevices: () =>
    getJson<{ devices: DeviceInfo[] }>("/api/cloud/devices/", { credentials: "include" }),

  createPairing: (deviceName: string) =>
    sendJson<PairingCreated>("/api/cloud/devices/pairing/", "POST", { deviceName }),

  pairingStatus: (id: string) =>
    getJson<PairingStatusInfo>(`/api/cloud/devices/pairing/${id}/`, { credentials: "include" }),

  revokeDevice: (id: number) => sendJson(`/api/cloud/devices/${id}/`, "DELETE"),

  getCloudSettings: () =>
    getJson<CloudSettings>("/api/cloud/settings/", { credentials: "include" }),

  updateCloudSettings: (defaultServerUsername: string) =>
    sendJson<{ default_server_username: string }>("/api/cloud/settings/", "PUT", {
      defaultServerUsername,
    }),

  listVault: () =>
    getJson<{ entries: VaultEntry[] }>("/api/cloud/vault/", { credentials: "include" }),

  addVaultEntry: (body: { address: string; username: string; password: string }) =>
    sendJson<VaultEntry>("/api/cloud/vault/", "POST", body),

  updateVaultEntry: (id: number, body: { username?: string; password?: string }) =>
    sendJson<VaultEntry>(`/api/cloud/vault/${id}/`, "PUT", body),

  deleteVaultEntry: (id: number) => sendJson(`/api/cloud/vault/${id}/`, "DELETE"),

  revealVaultEntry: (id: number) =>
    getJson<VaultSecretInfo>(`/api/cloud/vault/${id}/secret/`, { credentials: "include" }),

  listCharacters: () =>
    getJson<{ characters: CharacterInfo[] }>("/api/cloud/characters/", { credentials: "include" }),

  createCharacter: (body: {
    name: string;
    passwordType: string;
    password?: string;
    passwordList?: string[];
    skinUrl?: string;
  }) => sendJson<CharacterInfo>("/api/cloud/characters/", "POST", body),

  updateCharacter: (
    id: number,
    body: {
      name?: string;
      passwordType?: string;
      password?: string;
      passwordList?: string[];
      skinUrl?: string;
    }
  ) => sendJson<CharacterInfo>(`/api/cloud/characters/${id}/`, "PUT", body),

  deleteCharacter: (id: number) => sendJson(`/api/cloud/characters/${id}/`, "DELETE"),

  revealCharacter: (id: number) =>
    getJson<CharacterSecret>(`/api/cloud/characters/${id}/secret/`, { credentials: "include" }),

  uploadCharacterSkin: (id: number, form: FormData) =>
    sendForm<{ id: number; skin_url: string }>(`/api/cloud/characters/${id}/skin/`, form),

  /** 导出为 MC 64x64 皮肤 PNG(直链下载,带会话 cookie) */
  characterMinecraftSkinUrl: (id: number) =>
    `${API_BASE}/api/cloud/characters/${id}/skin/minecraft/`,

  // ---- 好友 ----

  listFriends: () =>
    getJson<{ friends: FriendInfo[] }>("/api/friends/", { credentials: "include" }),
  listFriendRequests: () =>
    getJson<{ incoming: FriendRequestInfo[]; outgoing: FriendRequestInfo[] }>(
      "/api/friends/requests/",
      { credentials: "include" }
    ),

  sendFriendRequest: (username: string) =>
    sendJson<{ success: boolean; accepted?: boolean }>("/api/friends/requests/", "POST", { username }),

  acceptFriendRequest: (id: number) =>
    sendJson(`/api/friends/requests/${id}/accept/`, "POST"),

  rejectFriendRequest: (id: number) => sendJson(`/api/friends/requests/${id}/`, "DELETE"),

  removeFriend: (username: string) => sendJson(`/api/friends/${username}/`, "DELETE"),

  blockUser: (username: string) => sendJson(`/api/friends/${username}/block/`, "POST"),

  unblockUser: (username: string) => sendJson(`/api/friends/${username}/block/`, "DELETE"),

  // ---- 私聊消息(网页端;实时推送走 WS dm.new) ----

  listMessages: (username: string, before?: number, limit = 50) =>
    getJson<{ messages: ChatMessage[]; hasMore: boolean }>(
      `/api/messages/${encodeURIComponent(username)}/${buildQuery({ before, limit })}`,
      { credentials: "include" }
    ),

  sendMessage: (username: string, body: string) =>
    sendJson<ChatMessage>(`/api/messages/${encodeURIComponent(username)}/`, "POST", { body }),

  unreadMessages: () =>
    getJson<{ unread: UnreadPeer[]; total: number }>("/api/messages/unread/", {
      credentials: "include",
    }),

  markMessagesRead: (username: string) =>
    sendJson<{ marked: number }>(`/api/messages/${encodeURIComponent(username)}/read/`, "POST"),

  /** 换取 WS 连接票据(60 秒一次性;跨域/CF 部署下 WS 握手无法带 cookie 时用) */
  wsTicket: () => sendJson<{ ticket: string }>("/api/cloud/client/ws-ticket/", "POST"),

  // ---- 联机:服务器大厅 / 组队 ----

  listServers: () =>
    getJson<{ servers: GameServerInfo[] }>("/api/servers/", { credentials: "include" }),

  adminListServers: () =>
    getJson<{ servers: AdminGameServerInfo[] }>("/api/servers/admin/", { credentials: "include" }),

  adminReviewServer: (id: number, body: { verified?: boolean; listed?: boolean }) =>
    sendJson(`/api/servers/admin/${id}/review/`, "POST", body),

  registerServer: (body: { address: string; name: string; description?: string; websiteUrl?: string }) =>
    sendJson<ServerReportToken>("/api/servers/", "POST", body),

  listMyServers: () =>
    getJson<{ servers: MyGameServerInfo[] }>("/api/servers/mine/", { credentials: "include" }),

  updateServer: (id: number, body: { name?: string; description?: string; websiteUrl?: string; listed?: boolean }) =>
    sendJson(`/api/servers/${id}/`, "PUT", body),

  deleteServer: (id: number) => sendJson(`/api/servers/${id}/`, "DELETE"),

  regenerateServerToken: (id: number) =>
    sendJson<ServerReportToken>(`/api/servers/${id}/token/`, "POST"),

  partyState: () =>
    getJson<{ party: PartyState | null }>("/api/party/", { credentials: "include" }),

  createParty: (serverAddress?: string) =>
    sendJson<PartyState>("/api/party/", "POST", { serverAddress }),

  joinParty: (code: string) => sendJson<PartyState>("/api/party/join/", "POST", { code }),

  leaveParty: () => sendJson("/api/party/leave/", "POST"),

  endParty: () => sendJson("/api/party/end/", "POST"),

  setPartyServer: (address: string) => sendJson("/api/party/server/", "POST", { address }),

  kickPartyMember: (username: string) => sendJson("/api/party/kick/", "POST", { username }),

  // ---- 管理:审计日志 ----

  auditLog: (n = 100) => getJson<AuditEntry[]>(`/api/audit/?n=${n}`, { credentials: "include" }),
};

// ---- 写请求辅助(带 cookie 会话)----

async function sendJson<T = unknown>(path: string, method: string, body?: unknown): Promise<T> {
  const res = await fetch(`${resolveBase()}${path}`, {
    method,
    credentials: "include",
    headers: { "Content-Type": "application/json", Accept: "application/json" },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  const text = await res.text();
  const data = text ? JSON.parse(text) : null;
  if (!res.ok) {
    const msg = data?.error ?? `请求失败: ${res.status}`;
    throw new Error(msg);
  }
  return data as T;
}

async function sendForm<T = unknown>(path: string, form: FormData): Promise<T> {
  const res = await fetch(`${resolveBase()}${path}`, {
    method: "POST",
    credentials: "include",
    body: form,
  });
  const text = await res.text();
  const data = text ? JSON.parse(text) : null;
  if (!res.ok) {
    const msg = data?.error ?? `请求失败: ${res.status}`;
    throw new Error(msg);
  }
  return data as T;
}

export interface PackageWriteBody {
  name?: string;
  title?: string;
  shortDescription?: string;
  longDescription?: string;
  type?: string;
  license?: string;
  mediaLicense?: string;
  repo?: string;
  website?: string;
  issueTracker?: string;
  forums?: number;
  videoUrl?: string;
  donateUrl?: string;
  translationUrl?: string;
  devState?: string;
  tags?: string[];
  contentWarnings?: string[];
}

export interface ScreenshotInfo {
  id: number;
  order: number;
  title: string;
  url: string;
  width: number;
  height: number;
  approved: boolean;
  is_cover_image?: boolean;
}

export interface ThreadSummary {
  id: number;
  title: string;
  is_private: boolean;
  locked: boolean;
  author: string;
  package?: string | null;
  created_at: string;
}

export interface ThreadReplyItem {
  id: number;
  comment: string;
  author: string;
  is_status_update: boolean;
  created_at: string;
}

export interface ThreadDetail extends ThreadSummary {
  can_comment: boolean;
  replies: ThreadReplyItem[];
}

export interface ReviewInfo {
  id?: number;
  author?: string;
  rating?: number;
  title?: string;
  comment?: string;
  language?: string;
}

export interface TokenInfo {
  id: number;
  name: string;
  createdAt: string;
  packageKey?: string | null;
}

export interface UserProfile {
  username: string;
  display_name?: string | null;
  rank?: string | null;
  website_url?: string | null;
  donate_url?: string | null;
  forums_username?: string | null;
  github_username?: string | null;
}

export interface AuditEntry {
  id: number;
  title: string;
  severity: string;
  url?: string | null;
  description?: string | null;
  causer?: string | null;
  package?: string | null;
  created_at: string;
}

export interface CollectionPackageItem {
  author: string;
  name: string;
  title?: string;
  short_description?: string | null;
  type?: string;
  thumbnail?: string | null;
  description?: string | null;
}

export interface CollectionInfo {
  author: string;
  name: string;
  title: string;
  short_description?: string | null;
  long_description?: string | null;
  private?: boolean;
  created_at?: string;
  package_count?: number;
  packages?: CollectionPackageItem[];
}

export interface NotificationItem {
  id: number;
  title: string;
  url: string;
  type: string;
  read: boolean;
  createdAt: string;
  causerUsername?: string | null;
  packageKey?: string | null;
}

// ---- 云同步 ----

export interface DeviceInfo {
  id: number;
  name: string;
  tokenPrefix: string;
  createdAt: string;
  lastUsedAt?: string | null;
}

export interface PairingCreated {
  id: string;
  code: string;
  deepLink: string;
  expiresAt: string;
}

export interface PairingStatusInfo {
  status: "pending" | "claimed" | "expired";
  device?: DeviceInfo | null;
}

export interface CloudSettings {
  default_server_username: string;
}

export interface VaultEntry {
  id: number;
  address: string;
  username: string;
  status: "ACTIVE" | "NEEDS_UPDATE";
  createdAt: string;
  updatedAt: string;
}

export interface VaultSecretInfo {
  id: number;
  address: string;
  username: string;
  password: string;
  updatedAt: string;
}

export type CharacterPasswordType = "FIXED" | "LIST_ROTATE" | "RANDOM";

export interface CharacterInfo {
  id: number;
  name: string;
  passwordType: CharacterPasswordType;
  skinUrl?: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface CharacterSecret {
  id: number;
  name: string;
  passwordType: string;
  password?: string | null;
  rotateIndex?: number | null;
  passwordListCount?: number | null;
  skinUrl?: string | null;
}

export interface FriendInfo {
  username: string;
  displayName?: string | null;
  profilePicUrl?: string | null;
  friendsSince: string;
  presenceAt?: string | null;
  currentServerAddress?: string | null;
  /** 游戏在线(服务器心跳,5 分钟窗口) */
  online: boolean;
  /** 网站在线(实时连接在线:网页/游戏客户端任一) */
  siteOnline: boolean;
}

export interface ChatMessage {
  id: number;
  from: string;
  fromDisplay?: string | null;
  to: string;
  body: string;
  createdAt: string;
  readAt?: string | null;
}

export interface UnreadPeer {
  username: string;
  displayName?: string | null;
  count: number;
  lastAt?: string | null;
}

export interface FriendRequestInfo {
  id: number;
  username: string;
  displayName?: string | null;
}

export interface GameServerInfo {
  id: number;
  address: string;
  name: string;
  description?: string | null;
  websiteUrl?: string | null;
  owner: string;
  playersOnline: number;
  playersMax: number;
  online: boolean;
  verified: boolean;
  ourPlayersOnline: number;
  reportedAt?: string | null;
}

export interface AdminGameServerInfo {
  id: number;
  address: string;
  name: string;
  owner: string;
  listed: boolean;
  verified: boolean;
  playersOnline: number;
  playersMax: number;
  online: boolean;
  createdAt: string;
}

export interface MyGameServerInfo {
  id: number;
  address: string;
  name: string;
  description?: string | null;
  websiteUrl?: string | null;
  playersOnline: number;
  playersMax: number;
  online: boolean;
  listed: boolean;
  verified: boolean;
  reportTokenPrefix?: string | null;
  createdAt: string;
}

export interface ServerReportToken {
  id: number;
  address: string;
  reportToken: string;
}

export interface PartyMemberInfo {
  username: string;
  displayName?: string | null;
  isLeader: boolean;
  online: boolean;
  currentServerAddress?: string | null;
}

export interface PartyState {
  id: number;
  code: string;
  leaderUsername: string;
  serverAddress?: string | null;
  createdAt: string;
  members: PartyMemberInfo[];
}

/** 构造建议下载文件名:{包名}_{版本}.zip(与后端 Content-Disposition 一致,作同源 <a download> 回退)。 */
export function downloadFileName(pkgName: string, version?: string | null): string {
  const sanitize = (s: string) =>
    s.replace(/[^a-zA-Z0-9._-]/g, "_");
  const pkg = sanitize(pkgName);
  return version ? `${pkg}_${sanitize(version)}.zip` : `${pkg}.zip`;
}
