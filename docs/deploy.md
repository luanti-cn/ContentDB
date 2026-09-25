# LuantiCN 生产部署文档(Docker)

> 架构:**luanti.cn** = Next.js 前端(容器 3000) · **api.luanti.cn** = ASP.NET 后端(容器 8080)
> 同一个仓库:`ContentDB/` 后端、`frontend/` 前端、`deploy/` 编排。
> 服务器上只需要装 **Docker**,配好一次后 `git push` 即自动上线。

---

## 一、架构

```
                ┌── luanti.cn ──────► Nginx ──► web 容器 (127.0.0.1:3000)
浏览器 ─────────┤                        (SSR 内部 fetch ──► api 容器:8080)
                └── api.luanti.cn ──► Nginx ──► api 容器 (127.0.0.1:5175)
                                                    │
LuantiCN 客户端(全部 API 走 api.luanti.cn)────────┤
                                                    │
                                        db 容器(PostgreSQL 16)+ 腾讯云 COS
```

- 数据库表结构在 api 容器启动时**自动迁移**,无需手动建表
- 容器端口只绑定 `127.0.0.1`,对外统一走 Nginx + HTTPS

## 二、服务器准备

```bash
# Docker + Compose 插件(Ubuntu 官方脚本)
curl -fsSL https://get.docker.com | sh

# Nginx
apt update && apt install -y nginx

# 部署用户与目录
useradd -m -s /bin/bash luanticn
mkdir -p /opt/luanticn && chown luanticn:luanticn /opt/luanticn
```

域名解析:把 `luanti.cn` 和 `api.luanti.cn` 的 A 记录指向服务器公网 IP。

## 三、代码与配置

```bash
su - luanticn
cd /opt/luanticn
git clone <你的仓库地址> contentdb
cd contentdb/deploy

# 生成环境配置
cp .env.example .env
openssl rand -base64 32     # 输出填到 VAULT_ENCRYPTION_KEY(生成后切勿更换)
nano .env                   # 填 POSTGRES_PASSWORD、COS/R2 的四个 STORAGE_* 值
```

`.env` 说明:

| 键 | 说明 |
|----|------|
| `POSTGRES_PASSWORD` | 容器内 PostgreSQL 密码 |
| `VAULT_ENCRYPTION_KEY` | 保管库加密密钥,**用了就不能换** |
| `STORAGE_*` | 对象存储(腾讯云 COS / R2)的地址、密钥、桶名 |

## 四、一条命令启动

```bash
cd /opt/luanticn/contentdb
docker compose -f deploy/docker-compose.yml --env-file deploy/.env up -d --build
```

首次会构建两个镜像(几分钟),之后:

```bash
docker compose -f deploy/docker-compose.yml ps        # 三个服务应为 running / healthy
curl http://127.0.0.1:5175/release_info.json          # API 验证
curl http://127.0.0.1:3000                            # Web 验证
```

## 五、Nginx + HTTPS

`/etc/nginx/sites-available/luanticn.conf`:

```nginx
server {
    listen 80;
    server_name api.luanti.cn;
    client_max_body_size 200m;              # 包/皮肤上传,防 413
    location / {
        proxy_pass http://127.0.0.1:5175;
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}

server {
    listen 80;
    server_name luanti.cn www.luanti.cn;
    client_max_body_size 20m;
    location / {
        proxy_pass http://127.0.0.1:3000;
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}
```

```bash
ln -s /etc/nginx/sites-available/luanticn.conf /etc/nginx/sites-enabled/
nginx -t && systemctl reload nginx
apt install -y certbot python3-certbot-nginx
certbot --nginx -d luanti.cn -d www.luanti.cn -d api.luanti.cn
```

> api 容器已启用 ForwardedHeaders,能拿到真实客户端 IP(geoip/统计依赖)。

## 六、push 即部署

仓库内置两个工作流(`deploy-api.yml` / `deploy-web.yml`):前后端**各自独立**触发,
SSH 到服务器执行 `git pull + docker compose up -d --build`,带健康检查。

GitHub 仓库 **Settings → Secrets and variables → Actions** 配 4 个机密:

| Secret | 值 |
|--------|---|
| `DEPLOY_SSH_HOST` | 服务器公网 IP |
| `DEPLOY_SSH_USER` | `luanticn` |
| `DEPLOY_SSH_PRIVATE_KEY` | 部署私钥(生成方式见下) |
| `DEPLOY_SSH_PORT` | SSH 端口(默认 22 可不配) |

```bash
# 服务器上生成部署密钥
ssh-keygen -t ed25519 -f ~/deploy_key -N ""
cat ~/deploy_key.pub >> ~/.ssh/authorized_keys
cat ~/deploy_key    # 私钥全文粘贴到 GitHub Secret

# 免密重启(容器编排其实不需要 systemctl,这行可省;保留以防回退宿主机部署)
```

部署用户需在 `docker` 组里:`sudo usermod -aG docker luanticn`(重新登录生效)。

另外 Android 包签名还需 4 个机密(见 `.github/workflows/android.yml`):
`ANDROID_KEYSTORE_BASE64` / `ANDROID_KEYSTORE_PASSWORD` / `ANDROID_KEY_ALIAS` / `ANDROID_KEY_PASSWORD`。

## 七、日常运维

```bash
cd /opt/luanticn/contentdb
docker compose -f deploy/docker-compose.yml ps          # 状态
docker compose -f deploy/docker-compose.yml logs -f api # 看 API 日志
docker compose -f deploy/docker-compose.yml up -d --build   # 手动全量重建
docker compose -f deploy/docker-compose.yml down        # 停止(数据在 pgdata 卷,不丢)
```

发新版(改 `appsettings.json` 的 `UpdateInfo` 版本号后 push)同样自动生效。

## 八、验证清单

```bash
curl https://api.luanti.cn/release_info.json
curl "https://api.luanti.cn/serverlists/list?proto_version_min=39&proto_version_max=53"
curl https://api.luanti.cn/serverlists/geoip
curl "https://api.luanti.cn/api/packages/?type=mod"
curl -sI https://luanti.cn/explore
```

客户端 LuantiCN:云同步配对 → 进服务器 → 内容库 → 服务器列表,全链路走 `api.luanti.cn`。

## 九、常见问题

| 现象 | 排查 |
|------|------|
| api 容器反复重启 | `docker compose logs api`:多半是 `.env` 没填好或 COS 密钥错 |
| 上传 413 | Nginx `client_max_body_size` 不够 |
| geoip 总返回 AS | 上游 `servers.luanti.org` 不可达(兜底生效,正常降级) |
| 保管库解密失败 | `VAULT_ENCRYPTION_KEY` 被更换(不可逆,保持一致) |
| 前端 SSR fetch 失败 | 检查 web 容器 `BACKEND_ORIGIN=http://api:8080`(容器内是 8080 不是 5175) |
| 磁盘涨 | `docker system prune -f` 清理旧镜像构建层 |
