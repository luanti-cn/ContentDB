# 客户端实现指南(LuantiCN 引擎内)

面向 LuantiCN 客户端(C++)开发者。API 细节见 [client-api.md](client-api.md),
总体设计见 [multiplayer.md](multiplayer.md)。本文讲**怎么在引擎里实现**。

原则:**全部功能直接实现在 Luanti 客户端内**(好友/私聊/组队/联机隧道),无外部 launcher。
所有云请求必须异步执行(curl 线程 / WS 线程),严禁阻塞主循环与渲染线程。

## 0. 总体架构

```
┌─────────────────────────────────────────────────┐
│ LuantiCN 客户端(C++ 引擎,LuantiCN 分叉)          │
│  ├─ src/cloud/  新增云服务模块                     │
│  │   · CloudConfig      device token / 后端地址   │
│  │   · CloudHttpClient   REST 封装(curl,Bearer)   │
│  │   · RealtimeClient    WebSocket 长连接          │
│  │   · StunClient        公网端点观测              │
│  │   · TunnelManager     打洞/中继/UDP 转发        │
│  │   · HostRoomController 开服房间注册/心跳         │
│  │   · JoinController    加入好友房间              │
│  ├─ 主菜单:好友 / 私聊 / 组队 三页签                │
│  ├─ 游戏内:私聊 toast + 聊天窗(HUD)              │
│  └─ 既有网络层(src/network/)协议不变              │
└───────────────────────┬─────────────────────────┘
                        │ HTTPS / WSS / UDP(隧道)
                        ▼
        后端(ASP.NET Core)+ Relay(UDP 中继)
```

## 1. 认证:设备配对

- 主菜单首次使用引导:打开网页「云同步 → 设备」生成配对码 → 在主菜单输入 8 位短码。
- `POST /api/cloud/client/pair/ {code, device_name}` → `deviceToken`(64 字符,仅此一次)。
- token 存入用户配置(`cloud.device_token`);文件权限按用户私有;
  泄露可在网页「设备管理」一键吊销。后续可升级 DPAPI/Keychain。
- 启动时 `GET /api/cloud/client/me/` 校验;401 → 清除 token 引导重新配对。

## 2. CloudHttpClient(REST 封装)

- 复用引擎内嵌 curl(`HTTPFetch` 同栈),统一加 `Authorization: Bearer <device_token>`。
- 统一错误处理:`{success:false, error}` → UI 提示;401 触发重新配对;429 提示稍后再试。
- 所有调用进线程池,回调贴回主线程再动 UI(与 `HTTPFetch` 完成回调模型一致)。

## 3. RealtimeClient(WebSocket 长连接)

- 端点:`wss://luanti.cn/api/cloud/client/ws/`,`Authorization: Bearer` 头认证。
- 实现:curl WebSocket API(`CURLOPT_CONNECT_ONLY=2`,curl ≥ 7.86;LuantiCN 需捆绑
  满足版本的 curl);不支持时降级为 30s REST 轮询(好友列表 + 未读数)。
- 收发循环独立线程;主线程通过无锁队列消费事件(UI 更新、toast、信令投递 TunnelManager)。
- 心跳:30s `{"type":"ping"}`;断线指数退避重连(1s/2s/4s…上限 60s);重连后重新 `room.host`/`room.join`。
- 事件分发给订阅者:`ChatPanel`(dm.new/dm.ack/dm.read)、`FriendsPanel`(presence)、
  `PartyPanel`(party.update)、`TunnelManager`(room.candidates/room.signal)。

## 4. 进服凭证(引擎内化既有流程)

```
进服(普通服务器):
1. POST /api/cloud/client/vault/provision/ {address, characterId?}
   → {username, password, created}
2. 用凭证连服(引擎内直接走既有连接流程,无需命令行)
3. 用户名被占 → 弹窗改名 → PUT /vault/ 回写 → 重试
4. 密码被改 → POST /vault/invalid/ → 提示输入/改名 → 回写 → 重试
```

## 5. 联机隧道(TunnelManager)

### 5.1 Host 流程(开本地游戏)

```
主菜单 "Host Game" / 启动本地服:
1. POST /host/register/ {status:"hosting", candidates:[stun…, local…]}
   → {roomId, roomCode, relay:{host,port,ticket}}
2. 向 relay:port 发注册包 "LRCN1|{roomId}|{ticket}"(ASCII),每 10s keepalive
3. WS room.host 持续上报候选;REST /host/heartbeat/ 每 30s 续期
4. 收到 room.signal(加入方打洞参数)→ TunnelManager 应答/配合打洞
5. 隧道数据到达 → 转发给 127.0.0.1:30000(本地服),回程对称
退服/关服:POST /host/close/(尽力而为;房间也会因超时自动过期)
```

### 5.2 Join 流程(加入好友)

```
好友面板「加入」(或输入房间码):
1. POST /host/join/ {username} 或 {roomCode}
   → {roomId, hostUsername, status, candidates, relay:{host,port}}   // 无 ticket
2. provision 进服凭证(§4,目标地址填 127.0.0.1 回环代理)
3. TunnelManager 起本地 UDP 回环代理(127.0.0.1:随机端口)
4. WS room.join 打开信令通道;与 Host 经 room.signal 交换打洞参数
5. 同步打洞(5-10s):成功 → P2P;失败(2 轮)→ 走中继
6. 引擎连接 127.0.0.1:随机端口 → 玩(后续全部透明)
断线:15s 无包 → 重打洞/切中继;房间消失 → 提示「好友已下线」
```

### 5.3 回环代理(单线程事件循环)

- 本地 socket(127.0.0.1:P)↔ 隧道 socket 的双向搬运;连接建立前先缓存首包(Luanti 握手)。
- 打洞窗口内双方向对方候选同步发包;`CONNECTED` 后只对单一对端收发。
- 中继模式下首包前先发注册包仅 Host 需要;Guest 直接发包即被中继记录。

### 5.4 打洞细节

- STUN(RFC 5389 仅 Binding):20 字节头 + MAGIC_COOKIE,解析 XOR-MAPPED-ADDRESS;
  向 2-3 个公共 STUN 发请求取多候选。
- 双方在信令确认后的同一秒窗口内互发探测包(各 5-8 个,间隔 100ms)以留足 NAT 映射。
- 对称 NAT(候选互相收不到)→ 直接切中继,不再重试。

## 6. 好友 / 私聊 / 组队 UI

- 主菜单三页签:
  - 好友:`GET /cloud/client/friends/` 初始 + WS presence 增量;在线者显示「私聊」「加入」「组队」。
  - 私聊:会话列表(未读数 `GET /messages/unread/`)+ 聊天窗(历史 `GET /messages/{u}/`
    游标翻页;发送走 WS `dm.send`,`dm.ack` 确认;`dm.new` toast + 未读红点)。
  - 组队:建房/凭码加入;`party.update` 推送驱动;队长切服时成员弹「跟随」提示。
- 游戏内:私聊 toast(右下角)+ 快捷打开聊天窗;聊天窗内 `/msg 好友名 内容` 也走云端私聊。

## 7. 服务器端上报 mod(不变)

服务器主安装 `luanticn_report` mod 上报在线状态,详见旧文档或 docs/multiplayer.md §2.5。
示例(`minetest.conf` 两行 + 每 60s HTTP 上报)保持不变:

```ini
secure.http_mods = luanticn_report
luanticn_report.token = ABCDEFGH23456789ABCDEFGH23456789
luanticn_report.address = play.example.com:30000
```

## 8. 节奏速查

| 事项 | 时机 / 频率 |
|------|-------------|
| WS 心跳 ping | 30s |
| WS 断线重连 | 指数退避,上限 60s |
| presence | 进服立即 + 每 60s + 退服 null(可全走 WS `presence`) |
| 服务器上报(服务端 mod) | 每 60s |
| Host 房间心跳 | REST 30s / 中继注册 keepalive 10s |
| 打洞窗口 | 5-10s;失败重试 ≤ 2 轮后切中继 |
| 隧道保活 | 10s 心跳;15s 判死重连 |
| 配对码 | 10 分钟有效,一次性 |

## 9. 安全清单

- [ ] device token 文件权限按用户私有;不进 git;提供「退出登录」清除入口
- [ ] 401 一律引导重新配对,不重试;配对码 410 提示重新生成
- [ ] 云请求全部异步,失败静默重试(节流),不弹窗轰炸
- [ ] 打洞/中继仅对好友开放(后端已强制);信令 payload 长度限制(如 4KB)
- [ ] 私聊内容本地不落盘(或加密),云端见 server 端策略
- [ ] 中继票据(ticket)仅存内存,不写日志不上报
