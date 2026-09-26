/** @type {import('next').NextConfig} */
const nextConfig = {
  reactStrictMode: true,
  images: {
    // 允许加载镜像/上游返回的缩略图与截图(按需在此追加你的对外域名/CDN)
    remotePatterns: [
      { protocol: "https", hostname: "**" },
      { protocol: "http", hostname: "localhost" },
    ],
  },
  async rewrites() {
    // 开发期把 /packages 代理到 C# 镜像后端,避免浏览器跨域。
    // 生产建议由反向代理(Nginx)统一分发,或设置 NEXT_PUBLIC_API_BASE 走绝对地址。
    // 注意:/api 认证 Cookie 经 rewrite 转发会在 Next 16 dev 下丢失,
    // 故 /api(及 OIDC 流)统一由 src/middleware.ts 透传,不在此配置。
    const backend = process.env.BACKEND_ORIGIN || "http://localhost:5175";
    return [
      // 仅代理"发布文件下载/临时链接"类后端端点:releases 后必须跟数字 release id + 动作。
      // 用 author/name 两段固定 + :id 限定,避免吞掉前端页面路由
      // (如 /packages/<a>/<n>/manage/releases 这类 manage 页,其 releases 在结尾且无 id)。
      {
        source: "/packages/:author/:name/releases/:id(\\d+)/:action*",
        destination: `${backend}/packages/:author/:name/releases/:id/:action*`,
      },
      { source: "/uploads/:path*", destination: `${backend}/uploads/:path*` },
      { source: "/thumbnails/:path*", destination: `${backend}/thumbnails/:path*` },
      // 认证与 OIDC 交互流(/login /logout /signin-oidc /signout-callback-oidc /oauth)
      // 由 src/middleware.ts 透传:rewrite 的 fetch 会跟随 302,导致登录页被
      // 吞进本站渲染、correlation cookie 丢失;middleware 用 redirect:"manual"
      // 原样返回后端的 302 与 Set-Cookie,让浏览器真正跳转到 OIDC 提供方。
      // feeds / metrics(可选,便于本地直接访问)
      { source: "/feed/:path*", destination: `${backend}/feed/:path*` },
    ];
  },
};

export default nextConfig;
