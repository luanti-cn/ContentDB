# Luanti 客户端接入 API(云同步)

面向 LuantiCN 客户端(引擎)开发者的接入文档。客户端通过**配对设备**获得 device token,之后可:

- 进服务器时自动取回/生成该服务器的账号密码(免手动输入)
- 管理云端角色(人物卡)
- 查看好友与在线状态、上报自己的所在服务器

## 通用约定

- **Base URL**:与站点一致(如 `https://luanti.cn`)
- **Content-Type**:`application/json`(皮肤上传除外)
- **认证**:除「配对」外,所有客户端接口需要请求头:

  ```
  Authorization: Bearer <device_token>
  ```

  device token 与网页会话/API Token 完全隔离,**只能访问 `/api/cloud/client/*`**。
- **错误格式**(统一):

  ```json
  { "success": false, "error": "错误说明(中文/英文)" }
  ```

  | 状态码 | 含义 |
  |--------|------|
  | 400 | 参数不合法 |
  | 401 | 未配对 / device token 无效或已吊销 |
  | 403 | 账号被封禁 / 被拉黑 |
  | 404 | 资源不存在(配对码 / 角色 / 好友等) |
  | 409 | 冲突(数量达上限、名字已存在、角色未设密码等) |
  | 410 | 配对码已使用或已过期 |
  | 413 | 文件过大 |

> 注意:所有路径以 `/` 结尾,缺失会 404。

---

## 1. 设备配对(一次性,无需认证)

用户在网页「云同步 → 设备」生成配对码,客户端通过以下任一方式获得:

- **扫码 / 深链**:`luanticn://login?code=XXXX-XXXX`
- **手动输入**:8 位短码(如 `ABCD-EF23`,大小写不敏感,连字符可省略)

配对码 **10 分钟有效、只能使用一次**。

```
POST /api/cloud/client/pair/
{ "code": "ABCD-EF23", "device_name": "我的电脑" }
```

`device_name` 可选;不传则用网页上预设的名字。

**响应 200:**

```json
{
  "deviceToken": "长随机串(64 字符)",
  "deviceId": 3,
  "deviceName": "我的电脑",
  "siteUsername": "steve123",
  "defaultServerUsername": "Steve"
}
```

- `deviceToken` **只在本次响应出现,请安全存储**(丢失只能吊销重新配对)
- `siteUsername`:站点用户名;`defaultServerUsername`:用户设置的进服默认名(未设置时等于站点用户名)

---

## 2. 设备会话

```
GET /api/cloud/client/me/
```

**响应 200:**

```json
{
  "device": { "id": 3, "name": "我的电脑", "last_used_at": "2026-09-25T12:00:00+08:00" },
  "user": {
    "username": "steve123",
    "display_name": "Steve",
    "default_server_username": "Steve"
  }
}
```

用途:启动时校验 token 有效性、展示登录身份。

---

## 3. 角色列表

```
GET /api/cloud/client/characters/
```

**响应 200:**

```json
{
  "characters": [
    {
      "id": 1,
      "name": "Steve",
      "passwordType": "RANDOM",
      "skinUrl": "/uploads/skin_ab12cd34ef.png",
      "createdAt": "...",
      "updatedAt": "..."
    }
  ]
}
```

`passwordType`:`FIXED`(固定)/ `LIST_ROTATE`(列表循环)/ `RANDOM`(随机)。
不含密码,仅用于进服前让用户选择「这次用哪个角色」。

---

## 4. 进服凭证(核心流程)

### 4.1 配给(推荐入口)

```
POST /api/cloud/client/vault/provision/
{ "address": "play.example.com:30000", "characterId": 1 }
```

- `address`:服务器地址。`host:port`、纯 host、带 `udp://` 前缀均可,服务端会规范化(小写 host、默认端口 30000 省略)
- `characterId`:可选。不传则用「默认用户名 + 随机密码」

**逻辑**:

- 该服务器已有凭证 → **原样返回**(`created=false`),密码列表游标不动
- 没有凭证 → 按角色密码策略生成新凭证并落库(`created=true`)

**响应 200:**

```json
{
  "address": "play.example.com",
  "username": "Steve",
  "password": "x7Km2pQr9sT4vB8w",
  "created": true,
  "characterId": 1,
  "characterName": "Steve"
}
```

可能错误:

| 状态码 | 场景 |
|--------|------|
| 404 | `characterId` 不存在 |
| 409 | 角色是 FIXED 但没设过密码 / 密码列表为空 / 默认用户名不合法 / 凭证数达上限 |

### 4.2 推荐的进服状态机

```
1. res = POST /vault/provision/ {address, characterId?}
2. 用 res.username / res.password 尝试在服务器注册(或登录)
3. 若服务器提示用户名已占用:
   a. 弹窗询问用户换一个名字(预填建议名)
   b. 用户确认后,用新名字 + 原 password 重试注册
   c. 成功后:PUT /vault/ 回写新用户名(见 4.6)
4. 注册/登录成功 → 进入服务器,并开始心跳(见第 6 节)
```

### 4.3 游戏内改密码的同步

Luanti 客户端菜单自带「Change Password」,改密动作发生在客户端本地 —— **客户端在改密成功的回调里,
立即把新密码同步到云端**:

```
PUT /api/cloud/client/vault/
{ "address": "play.example.com", "username": "Steve", "password": "新密码" }
```

无需任何额外接口。回写会自动把凭证状态恢复为正常。

**兜底:登录失败上报**(密码在未配对的设备/原版客户端上被改过、或被服管重置):

```
POST /api/cloud/client/vault/invalid/
{ "address": "play.example.com" }
```

- 云端将该凭证标记为「待更新」(网页端会显示提示),响应 `{ "success": true, "status": "NEEDS_UPDATE" }`
- 客户端应随后提示用户输入新密码,`PUT /vault/` 回写后状态自动恢复
- 走 4.4 的 upsert 语义;该地址无凭证时返回 404

### 4.5 查询某服务器的现有凭证

```
GET /api/cloud/client/vault/?address=play.example.com
```

- **200**:`{ "id": 9, "address": "play.example.com", "username": "Steve", "password": "...", "updatedAt": "..." }`
- **404**(无凭证):`{ "success": false, "error": "...", "address": "play.example.com", "default_username": "Steve" }`

`provision` 已覆盖此接口的能力,一般无需单独调用。

### 4.6 回写(注册成功 / 改名后 / 游戏内改密同步)

```
PUT /api/cloud/client/vault/
{ "address": "play.example.com", "username": "Steve_2", "password": "x7Km2pQr9sT4vB8w" }
```

**响应 200**:`{ "id": 9, "address": "play.example.com", "username": "Steve_2", "createdAt": "...", "updatedAt": "..." }`

upsert 语义:该地址没有凭证则新建(不校验凭证数上限)。

### 4.7 生成随机密码(可选)

```
GET /api/cloud/client/new-password/?length=16
```

**响应 200**:`{ "password": "x7Km2pQr9sT4vB8w" }`

服务端也可在配给时直接生成随机密码,此接口供客户端自行注册流程使用。`length` 8–64。

---

## 5. 包下载与临时直链

### 5.1 兼容下载端点

```
GET /packages/{author}/{name}/releases/{id}/download/?reason=new
```

与官方契约 1:1,Luanti 客户端可直接使用。行为按 User-Agent 区分:

- **Luanti/Minetest 客户端**(UA 以 `Luanti`/`Minetest` 开头):代理字节流,不做 302(规避客户端跨主机重定向问题)
- **其他**(浏览器等):302 跳转到对象存储的预签名直链,附带 `Content-Disposition` 文件名(`包名_版本.zip`)
- `reason`:可选,下载原因统计用(`new` / `update` / `dependency` 等)
- 计入下载统计

### 5.2 临时直链端点(temp-link)

```
GET /packages/{author}/{name}/releases/{id}/temp-link/
```

获取某个 release 在对象存储中的**临时(预签名)直链**,适合启动器/下载器绕过镜像直接从 CDN 高速拉取:

| 参数 | 说明 |
|------|------|
| `?format=json` | 返回 JSON `{ "url": "...", "expires_in": 3600 }`;默认行为是 302 直接跳转 |
| `?expires=秒数` | 覆盖链接有效期,上限 7 天(604800) |
| `?source=id` | 指定上游来源站点(多源部署时) |

示例:

```
GET /api/../packages/foo/bar/releases/12/temp-link/?format=json&expires=600
```

```json
{ "url": "https://cos.ap-guangzhou.myqcloud.com/...?sign=...", "expires_in": 600 }
```

- 预签名 url 同时附带建议文件名(response-content-disposition),保存时为 `包名_版本.zip`
- **不计入下载统计**(仅做链接解析;需要统计请用 5.1)
- 外链发布(无对象存储对象)时,`url` 为外部地址且无 `expires_in`

## 6. 好友 / 联机在线状态

### 6.1 好友列表(游戏内好友面板)

```
GET /api/cloud/client/friends/
```

**响应 200:**

```json
{
  "friends": [
    {
      "username": "alex",
      "displayName": "Alex",
      "profilePicUrl": null,
      "friendsSince": "2026-09-20T10:00:00+08:00",
      "presenceAt": "2026-09-25T12:00:30+08:00",
      "currentServerAddress": "play.example.com",
      "online": true
    }
  ]
}
```

- `online`:对方最近 5 分钟内有心跳即为 true
- `currentServerAddress`:对方正在玩的服务器(可直接做「加入好友所在服务器」)

### 6.2 心跳上报

```
POST /api/cloud/client/presence/
{ "address": "play.example.com" }
```

- **进服时、之后每 60 秒**上报一次当前所在服务器
- **退出服务器时**:`{ "address": null }`
- **响应 200**:`{ "success": true }`

不上报心跳超过 5 分钟,好友视角自动变离线。

---

## 7. 配额与限制

| 项 | 限制 |
|----|------|
| 每用户配对设备数 | 20(超出 409,需网页吊销后重试) |
| 每用户服务器凭证数 | 200 |
| 每用户角色数 | 50 |
| 角色密码列表 | 1–50 条,每条 ≤100 字符 |
| 皮肤图片 | jpg/png/webp,≤2 MiB(仅网页上传) |
| 配对码 | 10 分钟有效,一次性 |

## 8. 时序示例(完整进服)

```
# 首次使用:配对(网页扫码获得 code)
POST /api/cloud/client/pair/               → device_token(持久化)

# 之后每次启动
GET  /api/cloud/client/me/                 → 校验 token / 展示身份
GET  /api/cloud/client/characters/         → 角色选择 UI

# 用户点「加入 play.example.com」,选了角色 #1
POST /api/cloud/client/vault/provision/    → {username, password, created}
#   ↑ 重复进同一服务器返回相同凭证(created=false)

# 注册成功(若改名 → PUT /vault/ 回写)→ 进入服务器
POST /api/cloud/client/presence/           → 每 60s 心跳
GET  /api/cloud/client/friends/            → 好友面板(可选)

# 退出服务器
POST /api/cloud/client/presence/ {"address": null}
```

## 9. 皮肤分发(服务端 companion mod)

> 注意:本节端点**不属于** `/api/cloud/client/*`,无需 device token,匿名可读。
> 调用方是**游戏服务器**上安装的 companion mod(如 `cloud_skins`),不是客户端。

### 9.1 获取角色皮肤

```
GET /skins/{name}.png
```

- `{name}`:角色名,即该玩家在服务器内的用户名(合法字符 `[A-Za-z0-9_-]`,≤20)
- 返回 **PNG 字节流**(Luanti 皮肤布局,建议 64×64)
- 无角色 / 角色未设皮肤 / 对象缺失 → **404**(mod 应保持默认外观,不报错)
- 支持 `ETag` / `If-None-Match` 304;`Cache-Control: public, max-age=300`
- 同名角色可能属于不同站点用户:**取 `UpdatedAt` 最新者**(同名视为同一公开形象)

`SkinUrl` 为外链(http/https)时返回 302,mod 的 HTTP 客户端需跟随重定向。

### 9.2 推荐的 mod 行为

```
1. 玩家进服(register_on_joinplayer)
2. GET /skins/{player_name}.png
3. 200 → core.dynamic_add_media({filename=按内容哈希, filedata=字节,
                                 to_player=玩家, ephemeral=false})
   → 回调里 player:set_properties({textures = {filename}})
4. 404 / 网络失败 → 静默跳过(保留服务器默认外观)
5. 每次进服重新拉取(站端 max-age=300,皮肤更新最迟 5 分钟生效)
```

约束:

- mod 需要服务器在 `minetest.conf` 中授权:`secure.http_mods = cloud_skins`
- `filename` 必须含**内容哈希**:皮肤更新后文件名变化,才能绕过
  "同名 media 不可重复添加" 的限制
- `set_properties` 直接改默认 `character.b3d` 模型的贴图;自定玩家模型的
  游戏需自行适配贴图槽位

---

## 10. 实时通讯 WebSocket(好友私聊 / 在线推送 / 联机信令)

> 总体设计见 [multiplayer.md](multiplayer.md)。

### 10.1 连接

```
GET wss://luanti.cn/api/cloud/client/ws/
Authorization: Bearer <device_token>     (或网页 Cookie 会话)
Upgrade: websocket
```

- 一个用户可多条连接(网页 + 游戏客户端并存),推送发往全部连接
- 心跳:客户端每 30s 发 `{"type":"ping"}` → 服务端回 `{"type":"pong"}`;90s 无帧判死
- 断线重连:指数退避(1s/2s/4s…上限 60s)
- 连接建立后服务端立即发 `hello`,并向你的好友广播 presence 上线

### 10.2 消息协议(JSON,一帧一对象)

客户端 → 服务端:

| type | 载荷 | 说明 |
|------|------|------|
| `ping` | - | 心跳 |
| `dm.send` | `{to, body, clientId}` | 发私聊(仅好友);收到 `dm.ack` 确认 |
| `dm.read` | `{from}` | 标记来自某人的消息已读(对方收到 `dm.read` 回执) |
| `presence` | `{address?}` | 在线状态上报(等价 REST presence,null=退服) |
| `room.host` | `{roomId, status?, candidates?}` | Host 更新候选/状态(加入方收到 `room.candidates`) |
| `room.join` | `{roomId}` | 加入方打开房间信令通道(服务端校验好友)→ 回 `room.candidates` |
| `room.signal` | `{roomId, to, payload}` | P2P 打洞信令透传(payload 不解析,发同房间好友) |

服务端 → 客户端:

| type | 载荷 | 说明 |
|------|------|------|
| `hello` | `{user, deviceId}` | 连接确认 |
| `pong` | - | 心跳应答 |
| `dm.new` | `{from, fromDisplay, body, ts}` | 收到私聊 |
| `dm.ack` | `{clientId, id, to, ts}` | 发送确认(含服务端消息 id) |
| `dm.read` | `{by, ts}` | 对方已读回执 |
| `presence` | `{username, online, inGame, address?}` | 在线推送;`inGame=true` 为游戏状态(上下线/换服,address=服务器),`inGame=false` 为网站在线(WS 连接) |
| `friend.request` | `{from, fromDisplay}` | 收到好友申请实时提醒 |
| `friend.accepted` | `{username, displayName}` | 你的申请被接受(或双方成为好友) |
| `party.update` | `{party}` | 组队状态变更(替代 10s 轮询;party=null 表示已不在房间) |
| `room.candidates` | `{roomId, candidates?, status?, host?}` | Host 候选 |
| `room.signal` | `{roomId, from, payload}` | 信令透传 |
| `error` | `{code, message, refClientId?}` | 错误 |

### 10.3 私聊 REST(历史 / 离线兜底)

客户端前缀 `/api/cloud/client/messages/*`(device token);网页端 `/api/messages/*`(会话)。

```
GET  /api/cloud/client/messages/{username}/?before=<id>&limit=50   历史(倒序,游标分页)
POST /api/cloud/client/messages/{username}/   {"body": "..."}      发送(≤2000 字符,20 条/分钟)
GET  /api/cloud/client/messages/unread/                            未读计数
POST /api/cloud/client/messages/{username}/read/                   全部已读
```

- 仅好友可互发;被拉黑/封禁 → 403
- 历史:仅好友解除后仍可读(数据共享过);`hasMore=true` 时以最旧一条的 `id` 作 `before` 翻页

### 10.4 联机房间(P2P 打洞 + 中继,信令走 §10.2 WS)

```
POST /api/cloud/client/host/register/   {"status"?, "candidates"?}  → { roomId, roomCode, relay?, ... }
POST /api/cloud/client/host/heartbeat/  {"status"?, "candidates"?}  每 30s 续期;断 60s 房间过期
POST /api/cloud/client/host/close/                                  关闭房间
POST /api/cloud/client/host/join/       {"roomCode"} 或 {"username"} → { roomId, hostUsername, status, candidates, relay? }
```

- `candidates` 为任意 JSON(打洞候选数组,由客户端定义,后端透传不解析)
- `relay` 字段(中继兜底,部署了 ContentDB.Relay 时才有):
  - Host 收到:`{"host": "relay.luanti.cn", "port": 21005, "ticket": "..."}` —— **ticket 仅 Host 持有**,
    用它向中继端口发注册包 `LRCN1|{roomId}|{ticket}`(ASCII)
  - 加入方收到:`{"host": "relay.luanti.cn", "port": 21005}`(无 ticket)——直接往该端口发 UDP 包即可
- join 需与 Host 为好友;候选更新经 WS `room.host` → 成员收到 `room.candidates`
- 打洞/中继连接流程与时序见 [multiplayer.md](multiplayer.md) §2
