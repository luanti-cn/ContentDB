# C# 镜像服务架构与本轮改进

本仓库有三套应用：根目录的 Python 原版、`ContentDB/` 的 C# 镜像及本地业务服务、`frontend/` 的前端。本轮仅调整 C# 镜像的缓存与并发协调，保持公开 API 和数据库表结构不变。

## 缓存路径与生命周期

请求 → `MetadataCacheService` → L1 热缓存 → 按键互斥 → 再查 L1 → L2 数据库 → 上游 HTTP → 更新 L2/L1。

| 组件 | DI 生命周期 | 职责 |
| --- | --- | --- |
| `MetadataCacheService` | Scoped | 编排缓存查询、回源、响应改写与降级 |
| `MirrorDbContext` | Scoped | 持久化响应，不跨请求共享跟踪状态 |
| `MetadataResponseCache` | Singleton（每个应用容器） | 最多 500 条 LRU 热缓存；有效期取 L2 剩余时间与 60 秒的较小值 |
| `KeyedAsyncLock` | Singleton（每个应用容器） | 同键串行、异键独立；等待者取消或租约释放后减少引用，无引用时删除并释放信号量 |
| `TimeProvider` | Singleton | 可替换的时钟，确定性验证过期行为 |

移除静态全局集合后，不同应用实例和测试容器不会互相污染缓存。LRU 达到容量时逐条淘汰，不再清空整张热缓存。容量限制的是条目数，不是响应体总字节数。

缓存键使用版本前缀与结构化输入的 SHA-256，覆盖 source ID、上游地址、路径和查询、语言、User-Agent、URL 改写模式及改写目标。固定长度避免超出现有 512 字符数据库主键限制。更换域名或同一 API 请求原始/改写结果时不会读到另一种响应。旧键自然不再命中，会产生一次冷启动；旧缓存行可按过期时间清理，无需 schema migration。

取消请求应传播 `OperationCanceledException`，不转换成 502 或陈旧响应。普通网络异常及上游 5xx 可按配置返回陈旧缓存；4xx 保留上游语义。成功回源完成后再计算 TTL。L2 读写失败时仍能利用 L1；写失败会分离该键的跟踪实体，避免污染后续保存。

## 验证

```sh
dotnet build ContentDB/ContentDB.csproj --configuration Release
dotnet test tests/ContentDB.Infrastructure.Tests --configuration Release
```

回归测试覆盖 LRU 与过期边界、应用缓存隔离、错误不缓存、同键并发互斥、2000 个不同键释放、等待取消与重复释放、缓存键分区、跨请求作用域合并成功回源、慢回源 TTL、请求取消、数据库故障时的 L1、原始/改写响应隔离，以及 HTTP/网络异常降级。服务测试使用 EF InMemory，不代替 PostgreSQL 事务和多副本集成测试。

新增独立 C# CI，原 Python CI 保留。Infrastructure 的 ImageSharp 版本与 Web 项目统一到 3.1.12，避免不同入口解析出不同版本。

## 后续优先级

1. **文件镜像托管上传队列**：`FileMirrorService` 仍通过 `Task.Run` 上传，引用请求作用域内的存储服务；退出进程时不能可靠等待任务。应改为有背压的 `Channel` 加 `BackgroundService`，由消费者自建作用域，记录在途任务并让同键请求复用。必须保留首次下载无需等待对象存储上传的现有行为。当前只替换其永久增长的锁表，不声称已解决整个文件下载合并。
2. **多副本协调**：本轮锁只在单个进程内互斥。部署多个副本时，仍需数据库原子 upsert，以及适当的分布式租约，避免 L2 主键竞争与重复回源。
3. **进程内联机状态外置**：`RealtimeHub`、`WsTicketStore`、`HostRoomStore` 等仍是进程内单例。水平扩容需区分短期凭据、连接路由、房间持久状态，配合共享状态与发布订阅，不能直接复制单例集合到更多副本。
4. **启动迁移与业务分层**：生产迁移宜独立于 Web 实例启动；较大的控制器和维护任务应逐步抽出应用用例，明确事务边界和后台任务交付保证。保持模块化单体，暂不引入微服务运维成本。
