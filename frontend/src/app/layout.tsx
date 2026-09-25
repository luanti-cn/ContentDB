import type { Metadata } from "next";
import "./globals.css";
import { SiteHeader } from "@/components/site-header";
import { ThemeProvider } from "@/components/theme-provider";
import { ToastProvider } from "@/components/ui/toast";

export const metadata: Metadata = {
  title: "ContentDB 镜像",
  description: "Luanti 内容库国内镜像 —— 加速浏览与下载 mod、子游戏与材质包",
};

const __nameShim =
  "typeof __name==='undefined'&&(self.__name=function(f,n){try{Object.defineProperty(f,'name',{value:n,configurable:true})}catch{}});";

export default function RootLayout({
  children,
}: Readonly<{ children: React.ReactNode }>) {
  return (
    <html lang="zh-CN" suppressHydrationWarning>
      <body className="min-h-screen bg-background antialiased" suppressHydrationWarning>
        <script dangerouslySetInnerHTML={{ __html: __nameShim }} />
        <ThemeProvider
          attribute="class"
          defaultTheme="system"
          enableSystem
          disableTransitionOnChange
        >
          <ToastProvider>
            <SiteHeader />
            <main className="container py-8">{children}</main>
            <footer className="border-t py-6 text-center text-sm text-muted-foreground">
              ContentDB 国内镜像 · 数据来自 content.luanti.org
            </footer>
          </ToastProvider>
        </ThemeProvider>
      </body>
    </html>
  );
}
