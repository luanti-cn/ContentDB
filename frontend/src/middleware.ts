import { NextRequest, NextResponse } from "next/server";

const BACKEND_ORIGIN = process.env.BACKEND_ORIGIN ?? "https://api.luanti.cn";

export const config = {
  matcher: ["/login", "/logout", "/signin-oidc", "/signout-callback-oidc", "/oauth/:path*"],
};

export async function middleware(req: NextRequest) {
  const url = new URL(req.nextUrl.pathname + req.nextUrl.search, BACKEND_ORIGIN);

  const headers = new Headers();
  for (const name of ["cookie", "accept", "accept-language", "content-type", "user-agent", "referer"]) {
    const value = req.headers.get(name);
    if (value) headers.set(name, value);
  }

  const backend = await fetch(url, {
    method: req.method,
    headers,
    body: req.method === "GET" || req.method === "HEAD" ? undefined : req.body,
    redirect: "manual",
  });

  const resHeaders = new Headers();
  backend.headers.forEach((value, name) => {
    if (name.toLowerCase() !== "set-cookie") resHeaders.set(name, value);
  });
  for (const cookie of backend.headers.getSetCookie()) {
    resHeaders.append("set-cookie", cookie);
  }
  resHeaders.set("cache-control", "no-store");

  const location = resHeaders.get("location");
  if (location?.startsWith("/")) {
    resHeaders.set("location", new URL(location, req.url).toString());
  }

  return new NextResponse(backend.body, {
    status: backend.status,
    statusText: backend.statusText,
    headers: resHeaders,
  });
}
