# 联机体系设计(P2P 打洞 + 中继兜底 + WebSocket 消息)

目标:让普通玩家在自己电脑上开本地游戏,异地好友可一键加入;并提供游戏内好友面板与私聊。

所有功能**直接实现在 LuantiCN 客户端(C++ 引擎)内**,不依赖外部 launcher。

方案决策:
- 连接:**P2P 打洞为主,UDP 中继兜底**(打洞失败自动回退,对用户无感)
- 消息/社交:**引擎内实现**(WebSocket 长连接 + 引擎 UI,复用 device token 体系)
- 实时性:**WebSocket 长连接**(私聊/在线状态/组队变更/P2P 信令统一走一条 WS)

## 1. 总体架构

```
┌──────────────────────┐  WSS 长连接(信令/私聊/推送)  ┌────────────────────────┐
│ LuantiCN 客户端 A(开服)│◄───────────────────────────►│  ASP.NET Core 后端        │
│  ├ CloudService        │                              │  /api/cloud/client/ws/   │
│  │  · WS 客户端/推送    │                              │  (认证/消息/房间/推送)     │
│  │  · 好友/私聊/组队 UI │                              └───────────┬────────────┘
│  └ TunnelManager       │                                          │ 分配/登记
│     · 房间注册/心跳     │                                   ┌──────▼──────┐
│     · 隧道 + 中继注册   │                                   │ Relay 中继    │
│  Luanti 本地服         │                                   │ (独立进程,UDP)│
│  127.0.0.1:30000       │                                   └──────▲──────┘
└──────────────────────┘                                          │ 打洞失败时走中继
┌──────────────────────┐        P2P 直连(优先)                     │
│ LuantiCN 客户端 B(加入)│◄──── 127.0.0.1:随机端口 ◄── [引擎内 TunnelManager]
│  Luanti 引擎网络层      │         (打洞隧道 / 中继隧道)
└──────────────────────┘
```

关键设计:**加入方引擎内跑一个本地 UDP 回环代理**(绑定 `127.0.0.1:随机端口`),
引擎的连接代码只管连 localhost;代理把流量经"打洞成功的 UDP 隧道"或"中继"转发到开服方。
打洞/中继选择、断线重连全部发生在 TunnelManager 内部,对引擎其余部分透明。

## 2. 联机连接方案

### 2.1 角色

| 角色 | 说明 |
|------|------|
| Host(开服方) | 玩家 A 在本机开 Luanti 游戏("Host Game"/本地服,监听 UDP,如 30000);引擎注册房间 |
| Guest(加入方) | 玩家 B 通过好友面板/房间码加入;引擎起本地 UDP 回环代理 |
| 后端 | 房间登记、候选交换(信令)、中继端口分配、鉴权(仅好友可加入) |
| Relay(中继) | 独立进程,持有公网 IP;为每个房间分配 UDP 端口并转发流量 |

### 2.2 连接时序

```
A(Host)                    后端                        B(Guest)
  │ 开本地游戏(30000)          │                           │
  │ POST /host/register ──── │                           │
  │◄── roomId + roomCode      │                           │
  │    + relay{port,ticket}   │                           │
  │ 向中继端口发注册包 ──────────────────────────► Relay    │
  │ STUN 观测自身公网端点       │                           │
  │ WS: room.host {candidates}│                           │
  │   candidates = [           │                           │
  │     {type:stun, ...},      │                           │
  │     {type:local, ...}]     │                           │
  │                           │◄── POST /host/join ───────│
  │                           │─── candidates + relay ───►│
  │                           │   (仅好友;relay 无 ticket) │
  │ ◄═══════ WS 信令:交换/确认候选(room.signal) ════════► │
  │                           │                           │
  │ ◄──── P2P:双方同时向对方公网端点发包打洞(5-10s) ──────► │
  │        失败 → B 走中继 relay.luanti.cn:port            │
  │                           │                           │
A 隧道已就绪 ◄══════ Luanti UDP 流量 ════════► B 回环代理(127.0.0.1)
```

### 2.3 候选(Candidates)与端点观测

Host 侧提供两类候选,加入方按优先级尝试(`relay` 由后端单独下发,不在 candidates JSON 内):

| 候选 | 获取方式 | 说明 |
|------|----------|------|
| `stun` | 引擎内置 STUN 客户端(RFC 5389,仅 Binding)向公共 STUN 查询自身公网 IP:port | 打洞用;发多个 STUN 请求取多个候选 |
| `local` | 主机局域网地址 | 同局域网直连,延迟最低 |
| `relay` | 后端分配(见 §2.2,含 host/port;ticket 仅 Host 有) | 兜底,必给 |

### 2.4 打洞与隧道(引擎内 TunnelManager)

- 双方各持一个 UDP socket;经后端 WS 交换候选后,**同时**向对方候选发包(同步窗口内互发,
  各自 NAT 上留下映射),之后互为对端。
- 隧道保活:每 10s 互发心跳包;15s 无包判死 → 重打洞或切中继。
- Guest 回环代理把 `127.0.0.1:随机端口` 收到的引擎包经隧道发给 Host;Host 隧道收到后转发给
  `127.0.0.1:30000`(本地游戏);回程对称。状态机:`IDLE → PUNCHING → CONNECTED(P2P|RELAY)`。
- 打洞失败常见原因(对称 NAT):直接走中继,不重试超过 2 轮。

### 2.5 中继协议(Relay,独立进程 ContentDB.Relay)

- 控制面:HTTP(仅内网),`POST /allocate {roomId}`(内部密钥 `X-Relay-Secret` 鉴权)
  → 分配 `relayPort` + 一次性 `ticket`;`/deallocate` 释放;幂等;`GET /stats` 负载统计。
- 数据面:UDP。Host 向 `relay:port` 发注册包 `LRCN1|{roomId}|{ticket}` 后,中继记录 Host 端点;
  Guest 包到达即转发给 Host 端点,Host 包扇出给活跃 Guest 端点。空闲自动回收
  (未注册 30s / 空闲 60s / Guest 静默 30s);按房间限速。
- 简单性优先:不做加密(Luanti 协议自身有加密选项),只做端口分配 + 五元组转发。

### 2.6 中继横向扩展

架构天然按**房间分片**:一个房间的全部状态(端口、Host 端点、Guest 表)只存在于
一个 Relay 实例上,实例间零共享、零协调,扩容 = 加机器。

- 配置:后端 `Relay:Nodes[]` 数组(Name/BaseUrl/Secret/PublicHost);`Nodes` 为空时回落
  单节点平铺字段(合成为 default 节点)。
- 调度:`host/register` 时轮询选节点分配,失败自动换下一节点;房间记住所在节点,
  `join`/`close` 路由到同一节点;客户端只感知 `relay.host:port`(节点对外地址)。
- 故障:节点宕机 → 其上房间中断;Host 侧 30s 心跳失败 → 重注册自动落到其他节点;
  Guest 隧道断开后重新 join 即恢复(打洞失败也走新节点中继)。
- 限制:后端 `HostRoomStore`(房间登记表)仍是单进程内存态 —— 后端本身多实例部署时,
  需先给房间表加共享存储(如 Redis)或按用户做会话粘滞;这是后端扩展问题,与 Relay 无关。

## 3. WebSocket 网关(后端)

### 3.1 端点与认证

- 端点:`GET /api/cloud/client/ws/`(Upgrade: websocket)
- 认证:按顺序尝试
  1. `Authorization: Bearer <device_token>`(客户端,复用 DeviceTokenScheme)
  2. 网页 Cookie 会话(网页聊天页)
- 心跳:客户端每 30s 发 `{"type":"ping"}`,服务端回 `{"type":"pong"}`;90s 无任何帧判死断开。
- 一个用户可多条连接(网页 + 游戏客户端并存),广播发到该用户全部在线连接。

### 3.2 消息协议(JSON,一帧一对象)

客户端 → 服务端:

| type | 载荷 | 说明 |
|------|------|------|
| `ping` | - | 心跳 |
| `dm.send` | `{to, body, clientId}` | 发私聊(仅好友);服务端落库并转发 |
| `dm.read` | `{from}` | 标记来自某人的消息已读 |
| `presence` | `{address?}` | 心跳级在线状态上报(等同 REST presence) |
| `room.host` | `{roomId, candidates[], status}` | Host 广播候选/状态(开服中/已关闭) |
| `room.signal` | `{roomId, to, payload}` | P2P 信令透传(payload 不解析,打洞参数/握手确认) |
| `room.join` | `{roomId}` | 加入方进入房间信令通道(服务端校验好友关系) |

服务端 → 客户端:

| type | 载荷 | 说明 |
|------|------|------|
| `hello` | `{user, device}` | 连接建立确认 |
| `pong` | - | 心跳应答 |
| `dm.new` | `{from, fromDisplay, body, ts}` | 收到私聊 |
| `dm.ack` | `{clientId, ts}` | 发送确认(替代轮询) |
| `dm.read` | `{by, ts}` | 对方已读回执 |
| `presence` | `{username, online, inGame, address?}` | 在线推送;`inGame=true` 为游戏状态(上下线/换服),`inGame=false` 为网站在线 |
| `friend.request` | `{from, fromDisplay}` | 收到好友申请实时提醒 |
| `friend.accepted` | `{username, displayName}` | 你的申请被接受(或双方成为好友) |
| `party.update` | `{party}` | 组队房间任何变更(替代轮询) |
| `room.candidates` | `{roomId, candidates[], status}` | 把 Host 候选推给加入方 |
| `room.signal` | `{roomId, from, payload}` | 信令透传 |
| `error` | `{code, message, refType?, refClientId?}` | 错误 |

### 3.3 服务端结构

```
RealtimeHub(singleton)
 ├─ ConnectionManager:userId → set<WebSocket> + 元数据(设备/是否 host/所在房间)
 ├─ SendToUserAsync(userId, json)   // 全连接广播
 ├─ SendToRoomAsync(roomId, json, exceptUserId?)
 └─ 在线表:IsOnline(userId)(好友上下线判断的内存事实源)
```

- 上线:连入时对比好友列表,向所有好友广播 `presence online`;下线(全部连接断开)
  广播 `offline`。与既有 5 分钟心跳 presence 并存:WS 在线为准,离线回退心跳。
- 组队变更:PartyService 各写操作后调用 hub 推送 `party.update` 给房间成员。

## 4. 私聊消息

### 4.1 数据模型(AppDbContext)

```
direct_message
├─ id            bigserial PK
├─ sender_id     → user(id)
├─ recipient_id  → user(id)
├─ body          text(上限 2000 字符)
├─ created_at    timestamptz
├─ read_at       timestamptz?        -- 对方已读时间
└─ index (recipient_id, created_at desc), (sender_id, recipient_id, created_at desc)
```

### 4.2 REST(与 WS 并存,网页/历史/离线兜底)

| 端点 | 说明 |
|------|------|
| `GET /api/messages/{username}/?before=&limit=` | 与某好友的历史(游标分页,倒序) |
| `POST /api/messages/{username}/` | 发送(服务端也落库;WS 在线则同时推送) |
| `GET /api/messages/unread/` | 各好友未读计数 + 总数 |
| `POST /api/messages/{username}/read/` | 全部已读 |

客户端前缀 `/api/cloud/client/messages/*`(device token);网页端 `/api/messages/*`(会话)。

- 规则:仅好友可互发;被拉黑/封禁拒绝(403);单条 ≤ 2000 字符;发送频率限制(20 条/分钟)。

## 5. HostRoom(房间登记与信令 API)

| 端点 | 说明 |
|------|------|
| `POST /api/cloud/client/host/register/` | Host 开服登记 → `{roomId, roomCode, relay?}`;写内存表 + TTL |
| `POST /api/cloud/client/host/heartbeat/` | 每 30s 续期;断连 60s 后房间过期 |
| `POST /api/cloud/client/host/close/` | 关闭房间 |
| `POST /api/cloud/client/host/join/` | `{roomCode}` 或好友面板直接 `{username}` → 校验好友 → `{roomId, candidates, relay?}` |
| WS `room.host` / `room.signal` | 候选更新与打洞信令透传(见 §3.2) |

- `relay` 字段:Host 含 `{host, port, ticket}`(ticket 仅 Host 持有,用于向中继注册);
  加入方仅含 `{host, port}`(无法冒充 Host)。
- 房间是**内存态**(连接/进程生命周期),不落库;重启即空,Host 30s 内会重注册。
- 权限:仅好友可加入;房间码为辅助入口(分享给已是好友的人)。

## 6. LuantiCN 客户端实现要点(C++ 引擎内)

```
src/cloud/                      // 新增:云服务模块
├─ CloudConfig.cpp/.h           // device token 存取、后端地址
├─ CloudHttpClient.cpp/.h       // REST 封装(复用引擎 curl;Bearer 头)
├─ RealtimeClient.cpp/.h        // WS 长连接:心跳/重连/JSON 分发
├─ StunClient.cpp/.h            // 最小 STUN Binding(RFC 5389,取公网端点)
├─ TunnelManager.cpp/.h         // 打洞状态机 + 中继兜底 + UDP 转发
├─ HostRoomController.cpp/.h    // 开服登记/心跳/关闭(Host Game 时挂接)
└─ JoinController.cpp/.h        // 加入好友房间(好友面板「加入」入口)

src/gui/ (主菜单页签)            // 好友 / 私聊 / 组队 三页签
src/network/ (复用 socket 层)    // TunnelManager 的 UDP 收发
```

- **WS 实现**:基于 curl 的 WebSocket API(`CURLOPT_CONNECT_ONLY=2`,curl ≥ 7.86;
  LuantiCN 自带/捆绑的 curl 需满足版本;旧系统 curl 退化为 30s REST 轮询降级模式)。
- **JSON**:复用引擎内嵌的 jsoncpp。
- **device token 存储**:存入用户配置(`cloud.device_token`),文件权限按用户私有;
  后续可升级 OS 凭据库(DPAPI/Keychain)。
- **进服凭证**:沿用既有 provision 流程(`POST /vault/provision/` → 连服;
  失败改名回写 `PUT /vault/`;密码失效 `POST /vault/invalid/`)。
- **Host 流程**:主菜单 "Host Game"/本地服启动时 → `POST /host/register/` 拿
  `roomId/roomCode/relay` → 向中继端口发注册包并保持 keepalive → WS `room.host` 上报候选
  → 心跳线程(30s)维持房间;退服/关闭 → `POST /host/close/`。
- **Join 流程**:好友面板点「加入」→ `POST /host/join/` 拿候选 → TunnelManager 起回环代理
  (127.0.0.1 随机端口)→ 打洞(经 `room.signal` 交换参数,5-10s)→ 失败切中继 →
  引擎按普通进服流程连 `127.0.0.1:随机端口`(凭证照常 provision)。
- **好友/私聊/组队 UI**:主菜单三页签 + 游戏内 HUD(收信 toast / 快捷回复);
  WS 推送驱动,REST 历史/离线兜底;组队 `party.update` 推送驱动跟随。

## 7. 引擎改动清单(相对上游 Luanti)

| 模块 | 内容 |
|------|------|
| `src/cloud/`(新增) | 上述云服务模块(WS/REST/STUN/隧道/房间) |
| 主菜单 | 「好友 / 私聊 / 组队」页签;Host Game 挂接房间注册 |
| 游戏内 UI | 私聊 toast + 聊天窗(私聊走云端,房间聊天仍走服务器) |
| `src/network/` | 无协议改动;TunnelManager 复用 UDP socket 层 |
| (可选) | 改密同步等既有方案 A 文件接力不变 |

引擎直连云端 —— 好处是单进程、部署简单;代价是引擎内要维护 WS/重连/鉴权,
注意所有云请求都必须异步(curl 线程),严禁阻塞主循环/渲染线程。

## 8. 安全与滥用防护

- WS/REST 全部走既有 device token / 会话鉴权;跨用户信令仅好友可达。
- device token 在客户端本地为明文可读(游戏客户端通用现状):按用户私有文件权限保存,
  泄露可在网页「设备管理」一键吊销;后续升级 OS 凭据库。
- 中继按房间限速 + 端口空闲回收;allocate 仅后端内部密钥可调。
- 私聊内容为隐私数据:不分词不入搜索;举报入口沿用站内举报;封禁用户全局断连。
- 房间码泄漏风险:默认仅好友可加入;后续如开"邀请链接",加一次性 token + 有效期。

## 9. 实施阶段

| 阶段 | 内容 | 依赖 |
|------|------|------|
| P1 后端基础 | DirectMessage 表 + DM 服务/REST + WS 网关 + 推送 | ✅ 已完成 |
| P2 房间与信令 | HostRoom API + WS 信令透传 | ✅ 已完成 |
| P3 中继 | ContentDB.Relay 进程 + allocate 内部 API | ✅ 已完成 |
| P4 引擎:基础 | CloudService(device token/REST/WS)+ 主菜单三页签 UI | P1-P2 |
| P5 引擎:联机 | StunClient + TunnelManager(打洞/中继/回环代理)+ Host/Join 流程 | P3-P4 |
| P6 网页 | 网页端聊天页(复用 DM REST + WS cookie 认证) | P1 |
