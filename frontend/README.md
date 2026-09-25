# ContentDB 镜像前端

Luanti 内容库国内镜像的 Web 前端。技术栈:**Next.js 15 (App Router) + TypeScript + Tailwind CSS + shadcn/ui**。

对接后端的 C# 镜像服务(`../ContentDB`),复用其只读 API(本地自营 + 官方上游合并视图),并支持下载与临时链接端点。

## 目录结构

```
frontend/
├─ src/
│  ├─ app/
│  │  ├─ layout.tsx                 # 全局布局 + 站点头/脚
│  │  ├─ globals.css                # Tailwind + shadcn 主题变量
│  │  ├─ page.tsx                   # 首页/列表(?type=&q=)
│  │  └─ packages/[author]/[name]/page.tsx  # 包详情 + 下载
│  ├─ components/
│  │  ├─ ui/                        # shadcn/ui 组件(button/card/badge/input/separator)
│  │  ├─ site-header.tsx
│  │  └─ package-card.tsx
│  └─ lib/
│     ├─ api.ts                     # 镜像 API 客户端 + 类型
│     └─ utils.ts                   # cn() 工具
├─ components.json                  # shadcn/ui 配置(可继续 `npx shadcn add ...`)
├─ tailwind.config.ts
└─ next.config.mjs                  # 开发期把 /api 等反代到 C# 后端
```

## 快速开始

```bash
cd frontend
cp .env.example .env
npm install
npm run dev        # http://localhost:3000
```

需要同时运行 C# 镜像后端(默认 `http://localhost:5175`)。`next.config.mjs` 会把
`/api`、`/packages/.../releases`、`/uploads`、`/thumbnails` 反代到 `BACKEND_ORIGIN`,
因此前端与后端同源,无跨域问题。

## 与后端的对接点

| 前端调用 | 后端端点 |
|---|---|
| `api.listPackages()` | `GET /api/packages/`(合并本地+上游,含 `origin` 字段)|
| `api.getPackage()` | `GET /api/packages/{author}/{name}/` |
| `api.listReleases()` | `GET /api/packages/{author}/{name}/releases/` |
| 下载按钮 | `GET /packages/{author}/{name}/releases/{id}/download/` |
| 临时链接 | `GET /packages/{author}/{name}/releases/{id}/temp-link/?format=json` |

`origin` 字段是本镜像的扩展:`local`(本站自营)/`upstream`(官方上游镜像),
前端用徽章区分展示。

## 追加 shadcn/ui 组件

项目已内置常用基础组件。要新增其他组件:

```bash
npx shadcn@latest add dialog table pagination ...
```

`components.json` 已配置好别名(`@/components/ui`、`@/lib/utils`)与主题(zinc / new-york)。

## 生产部署

- `npm run build && npm run start`,或用 Vercel / Node 容器。
- 生产环境建议由 Nginx 统一分发:前端静态/SSR 与后端 `/api`、`/packages`、`/uploads` 同域,
  或设置 `NEXT_PUBLIC_API_BASE` 指向后端对外地址。
