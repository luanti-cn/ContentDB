# ContentDB 国内镜像 (C# / ASP.NET Core)

Luanti(原 Minetest)官方 [ContentDB](https://content.luanti.org) 的国内镜像 / 代理 / 缓存服务。
用 C# + ASP.NET Core 重写(第一阶段:只读镜像),为国内用户提供加速访问。

## 它做什么

- **只读 API 镜像**:1:1 兼容官方 `/api/*` JSON 契约,Luanti 客户端可直接把 ContentDB 地址指向本镜像。
- **按需代理 + 懒缓存**:客户端请求时回源官方,首次拉取后缓存元数据到 PostgreSQL、文件到 S3;后续走本地缓存并定期刷新。
- **S3 兼容存储**:release zip 与截图存入对象存储,支持 AWS S3 / 阿里云 OSS / 腾讯云 COS / MinIO。
- **下载重定向**:`/packages/<author>/<name>/releases/<id>/download/` 首次同步拉取入 S3,然后 302 到 CDN。
- **容错降级**:回源失败时可返回陈旧缓存(可配置),保证可用性。
- **预热任务**:后台 `BackgroundService` 定期预热热门端点。

## 架构

```
Luanti 客户端(国内)
      │
      ▼
[ASP.NET Core 镜像服务]
      ├── ContentDB            (Api:控制器/后台任务/入口)
      ├── ContentDB.Core       (抽象/配置/DTO/URL 改写)
      └── ContentDB.Infrastructure (EF Core/S3/上游回源/缓存与文件服务)
      │
      ├── PostgreSQL   缓存元数据 (cached_response, mirrored_file)
      └── S3 兼容存储   zip + 截图
      │
      ▼ (download 302)
   S3 / CDN
```

## 分层职责

| 项目 | 职责 |
|------|------|
| `ContentDB` (Api) | 控制器(`ApiMirrorController`、`DownloadController`)、后台服务、`Program.cs` |
| `ContentDB.Core` | `IObjectStorage` / `IUpstreamClient` / `IMetadataCacheService` / `IFileMirrorService` 抽象、`MirrorOptions` / `StorageOptions` 配置、`UrlRewriter` |
| `ContentDB.Infrastructure` | EF `MirrorDbContext`、`S3ObjectStorage`、`UpstreamClient`(Polly)、`MetadataCacheService`、`FileMirrorService`、DI 注册 |

## 配置(appsettings.json)

```jsonc
{
  "ConnectionStrings": {
    "MirrorDb": "Host=localhost;Database=contentdb_mirror;Username=contentdb;Password=password"
  },
  "Mirror": {
    "UpstreamBaseUrl": "https://content.luanti.org", // 回源官方地址
    "PublicBaseUrl": "http://localhost:5175",          // 本镜像对外域名(用于改写返回的绝对 URL)
    "UpstreamTimeoutSeconds": 30,
    "ServeStaleOnUpstreamError": true,                 // 回源失败降级返回旧缓存
    "ForwardUserAgent": true,
    "MetadataTtlSeconds": { "Packages": 300, "Updates": 300, "Tags": 3600, "Default": 300 }
  },
  "Storage": {
    "ServiceUrl": "http://localhost:9000",  // S3 兼容 endpoint(OSS/COS/MinIO);留空用 AWS 默认
    "Region": "us-east-1",
    "AccessKey": "minioadmin",
    "SecretKey": "minioadmin",
    "Bucket": "contentdb",
    "ForcePathStyle": true,                 // MinIO/自建需 true
    "PublicUrlBase": "http://localhost:9000/contentdb" // 文件对外 URL(CDN 域名);留空则由镜像代理文件流
  }
}
```

> 生产环境请用环境变量 / User Secrets 注入密钥,勿提交明文。

## 运行

前置:PostgreSQL、S3 兼容存储(可用 MinIO 快速起步)。

```sh
# MinIO(本地测试)
docker run -p 9000:9000 -p 9001:9001 minio/minio server /data --console-address ":9001"

# 运行镜像服务(首次启动会 EnsureCreated 自动建表)
dotnet run --project ContentDB.csproj
```

访问:
- 健康检查:`GET /healthz`
- 包列表:`GET /api/packages/`
- 更新检查:`GET /api/updates/?protocol_version=47`
- 下载:`GET /packages/<author>/<name>/releases/<id>/download/`

## 生产部署要点(国内)

1. **反向代理**:置于 Nginx/CDN 后,已启用 `UseForwardedHeaders`(读取 `X-Forwarded-For`/`Proto`);生产应把代理 IP 加入 `KnownProxies`。
2. **HTTPS**:证书在反代层终止。
3. **ICP 备案**:国内公网提供服务需域名 + 服务器备案。
4. **`PublicBaseUrl` / `Storage.PublicUrlBase`**:务必设为真实对外域名,否则客户端仍会拿到官方或错误 URL。
5. **数据库迁移**:起步用 `EnsureCreated`;正式环境建议改用 EF Core Migrations。

## 已知事项

- 传递依赖 `Microsoft.OpenApi 2.0.0`(来自 `Microsoft.AspNetCore.OpenApi`)有安全告警(NU1903)。OpenAPI 仅开发环境启用;可在依赖更新后钉到修复版本消除告警。
- 第一阶段仅实现**只读镜像**。写操作(上传/编辑/审核)、用户系统、OAuth、Web UI 等为后续阶段。

## 云同步:设备配对 + 服务器账号保管库

> Luanti 客户端/启动器接入文档见 [docs/client-api.md](docs/client-api.md);**实现指南(架构/代码/服务器 mod)见 [docs/client-implementation.md](docs/client-implementation.md)**。

用户在 luanti.cn 网页绑定设备后,Luanti 客户端(配对设备)可从云端取回/回写各游戏服务器的登录凭证,免去手动输入账号密码。

**网页端 API(会话/API Token):**

| 端点 | 说明 |
|------|------|
| `GET /api/cloud/devices/` | 已配对设备列表 |
| `POST /api/cloud/devices/pairing/` | 创建配对(`{device_name}` → 短码 + `luanticn://login?code=X` 深链,网页据此渲染二维码) |
| `GET /api/cloud/devices/pairing/{id}/` | 轮询配对状态(pending/claimed/expired) |
| `DELETE /api/cloud/devices/{id}/` | 吊销设备 |
| `GET/PUT /api/cloud/settings/` | 默认游戏内用户名(空 = 回落站点用户名) |
| `GET/POST/PUT/DELETE /api/cloud/vault/...` | 保管库管理;`GET /{id}/secret/` 查看明文密码 |

**客户端 API(配对后的 device token,Bearer):**

| 端点 | 说明 |
|------|------|
| `POST /api/cloud/client/pair/` | `{code, device_name?}` → `{device_token, ...}`(一次性短码,10 分钟有效) |
| `GET /api/cloud/client/me/` | 设备与用户信息 |
| `GET /api/cloud/client/new-password/` | 生成随机密码(注册新服账号用) |
| `GET /api/cloud/client/vault/?address=host:port` | 取凭证;404 时返回 `default_username` 建议 |
| `PUT /api/cloud/client/vault/` | 注册/改名成功后回写 upsert |

**客户端进服逻辑(参考):**

1. `GET /vault/?address=` → 200:直接用返回的 username/password 连服;
2. 404 → 用 `default_username` + `GET /new-password/` 的随机密码在服务器注册;若用户名被占用,提示用户改名后重试;
3. 注册/改名成功 → `PUT /vault/` 回写云端。

**安全:** 密码以 AES-256-GCM 加密落库,密钥 `Vault:EncryptionKey`(32 字节 base64)必须经环境变量/User Secrets 注入,更换后旧密文不可解;device token 仅存 SHA-256,明文只在配对成功时返回一次,可随时在网页吊销。

**角色(人物卡):**

用户创建云端角色,进新服务器时客户端指定角色配给凭证:

| 端点(网页端) | 说明 |
|------|------|
| `GET/POST /api/cloud/characters/` | 列出/创建角色 `{name, password_type, password?, password_list?, skin_url?}` |
| `PUT/DELETE /api/cloud/characters/{id}/` | 修改/删除角色 |
| `GET /api/cloud/characters/{id}/secret/` | 查看密码(FIXED=固定值;LIST_ROTATE=下一条;RANDOM=null) |
| `POST /api/cloud/characters/{id}/skin/` | 上传皮肤图片(multipart `file`,jpg/png/webp,≤2 MiB);`?convert=auto` 时 64x64 的 MC 皮肤自动转换为 Luanti 64x32(合并覆盖层) |
| `GET /api/cloud/characters/{id}/skin/minecraft/` | 导出为 Minecraft 64x64 皮肤 PNG(64x32 自动补全左臂/左腿) |
| `GET /api/cloud/client/characters/` | 客户端:角色列表(不含密码) |

**密码类型(password_type):**

- `FIXED` — 固定密码,用户自设,所有新服务器共用;
- `LIST_ROTATE` — 密码列表循环,每配给一个新服务器取下一条,用完从头循环;
- `RANDOM` — 每个新服务器随机生成一条(默认)。

**客户端进服流程(更新):** `POST /api/cloud/client/vault/provision/ {address, character_id?}` —
已有该服务器凭证则原样返回(`created=false`);否则按角色策略生成并落库(`created=true`)。
服务器上用户名被占用时,客户端询问用户改名后仍用 `PUT /api/cloud/client/vault/` 回写。

**好友 / 联机在线状态:**

| 端点(网页端) | 说明 |
|------|------|
| `GET /api/friends/` | 好友列表(含在线状态、当前所在服务器) |
| `GET /api/friends/requests/` | 收到/发出的申请 |
| `POST /api/friends/requests/` | 发申请 `{username}`;对方已先申请我则直接成为好友 |
| `POST /api/friends/requests/{id}/accept/` | 接受 |
| `DELETE /api/friends/requests/{id}/` | 拒绝 / 撤回 |
| `DELETE /api/friends/{username}/` | 删除好友 |
| `POST/DELETE /api/friends/{username}/block/` | 拉黑 / 取消拉黑 |

| 端点(客户端 device token) | 说明 |
|------|------|
| `GET /api/cloud/client/friends/` | 游戏内好友面板:在线状态 + 所在服务器 |
| `POST /api/cloud/client/presence/` | 心跳 `{address}`(建议 60s 一次;退服传 null),超 5 分钟无心跳视为离线 |

**服务器大厅 / 组队(联机):**

| 端点(网页端) | 说明 |
|------|------|
| `GET /api/servers/` | 公开服务器列表(无需登录;含实时人数与"本站用户在线数") |
| `POST /api/servers/` | 收录服务器 → 返回上报 token(明文仅此次) |
| `GET/PUT/DELETE /api/servers/{id}/`、`GET /mine/`、`POST /{id}/token/` | 管理 |
| `POST /api/servers/report/` | 服务器端 mod 上报 `{address, token, players_online, players_max, motd}`(60 秒一次,10 分钟无上报视为离线) |
| `GET/POST /api/party/` | 组队房间状态 / 创建(`{server_address?}`) |
| `POST /api/party/join/`、`/leave/`、`/end/`、`/server/`、`/kick/` | 凭 6 位邀请码进房;队长设目标服/踢人/解散;队长退出即解散 |

| 端点(客户端 device token) | 说明 |
|------|------|
| `GET /api/cloud/client/servers/` | 游戏内服务器浏览器 |
| `GET /api/cloud/client/party/` | 轮询房间状态(成员在线 + 目标服,10 秒一次) |
| `POST /api/cloud/client/party/(join|leave|end|server|kick)/` | 同网页端 |

好友上线通知:设备心跳从离线转为在线时,自动给所有 ACCEPTED 好友发 `FRIEND_ONLINE` 通知。

## 后续阶段(规划)

- 全量元数据预同步(导入官方每日 DB dump 做冷启动种子)
- Redis 缓存层加速(替代/补充 DB 缓存)
- Prometheus 指标 + 回源失败告警
- 写入侧 API 与用户系统(如需独立运营)
