# ContentDB.Relay

联机 UDP 中继(P2P 打洞失败时的兜底)。独立进程,需部署在**有公网 IP** 的机器上。

## 职责

- 控制面(HTTP,仅内网):主后端持密钥调用 `/allocate` 为联机房间分配一个 UDP 端口
- 数据面(UDP):每房间一个端口;Host 持票据注册后双向转发 Luanti 流量
- 自愈:未注册宽限 30s;房间空闲 60s;Guest 静默 30s —— 均自动回收,无需后端清理

## 转发规则

```
Host 端点发包 → 扇出给全部活跃 Guest
未知端点(Guest)发包 → 记为 Guest,转发给 Host
注册包:ASCII "LRCN1|{roomId}|{ticket}"(Host 首包,票据由 /allocate 返回,仅 Host 持有)
其余包原样转发,内容不解析
```

## 运行

```sh
# 最小配置(appsettings.json 或环境变量)
Relay__InternalSecret=<与主后端 Relay__Secret 一致的长随机串>
Relay__PublicAddress=relay.luanti.cn          # 客户端可连的公网地址

dotnet run --project ContentDB.Relay
```

控制面端点(全部要求 `X-Relay-Secret` 头,`/healthz` 除外):

| 端点 | 说明 |
|------|------|
| `POST /allocate {"roomId"}` | 分配端口(幂等)→ `{port, publicAddress, ticket}` |
| `POST /deallocate {"roomId"}` | 释放端口 |
| `GET /rooms` | 房间列表(运维) |
| `GET /stats` | 负载统计 `{rooms, hosted, guests}`(横向扩展调度用) |

## 横向扩展

按**房间分片**:一个房间的全部状态只在一个实例上,实例间零共享 —— 直接多机多实例部署即可,
无需 Redis/分布式协调。

1. 每台机器跑一个 ContentDB.Relay(各自独立端口池,如都留 20000-25000,互不冲突)
2. 后端配置 `Relay:Nodes[]`(Name/BaseUrl/Secret/PublicHost),`register` 时轮询分配、
   失败自动换节点;房间记住所在节点,`join`/`close` 路由回同一节点
3. 客户端只连节点自己的 `PublicHost`(可用不同域名/线路,如电信/联通分线路接入)
4. 节点宕机:其上房间中断;Host 心跳失败自动重注册到其他节点,Guest 重新 join 即恢复

```jsonc
// 后端 appsettings 多节点示例
"Relay": {
  "Enabled": true,
  "Nodes": [
    { "Name": "relay-bj-1", "BaseUrl": "http://10.0.0.5:5180", "Secret": "<密钥>", "PublicHost": "relay-bj.luanti.cn" },
    { "Name": "relay-gz-1", "BaseUrl": "http://10.0.0.6:5180", "Secret": "<密钥>", "PublicHost": "relay-gz.luanti.cn" }
  ]
}
```

## 安全

- 未配置 `InternalSecret` 时拒绝所有分配
- 票据仅返回给主后端(转交 Host);Guest 无票据无法冒充 Host 注册
- 每房间限速(默认 5 Mbps)、房间数上限(默认 500)、Guest 数上限(默认 8)
- 控制面务必置于内网/防火墙后,不要暴露公网
