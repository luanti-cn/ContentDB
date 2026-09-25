import Link from "next/link";
import { notFound } from "next/navigation";
import { api, downloadFileName } from "@/lib/api";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Separator } from "@/components/ui/separator";
import { PackageOwnerActions } from "@/components/package-owner-actions";
import { PackageReviews } from "@/components/package-reviews";
import { PackageThreads } from "@/components/package-threads";
import { Download, ExternalLink, Star } from "lucide-react";

export const dynamic = "force-dynamic";

function formatSize(bytes: number): string {
  if (!bytes) return "-";
  const kb = bytes / 1024;
  return kb > 1024 ? `${(kb / 1024).toFixed(1)} MB` : `${Math.round(kb)} KB`;
}

export default async function PackagePage({
  params,
}: {
  params: Promise<{ author: string; name: string }>;
}) {
  const { author, name } = await params;

  let pkg;
  let releases;
  try {
    [pkg, releases] = await Promise.all([
      api.getPackage(author, name),
      api.listReleases(author, name).catch(() => []),
    ]);
  } catch {
    notFound();
  }

  const latest = releases[0];

  return (
    <div className="grid grid-cols-1 gap-8 lg:grid-cols-3">
      <div className="space-y-6 lg:col-span-2">
        <div className="space-y-2">
          <div className="flex flex-wrap items-center gap-2">
            <h1 className="text-3xl font-bold tracking-tight">{pkg.title}</h1>
            {pkg.origin === "upstream" ? (
              <Badge variant="secondary">上游</Badge>
            ) : (
              <Badge>本站</Badge>
            )}
          </div>
          <p className="text-muted-foreground">
            by{" "}
            <Link href={`/?author=${pkg.author}`} className="hover:underline">
              {pkg.author}
            </Link>
          </p>
          <p className="text-lg">{pkg.short_description}</p>
        </div>

        {pkg.screenshots && pkg.screenshots.length > 0 && (
          <div className="grid grid-cols-2 gap-3">
            {pkg.screenshots.slice(0, 4).map((src) => (
              // eslint-disable-next-line @next/next/no-img-element
              <img
                key={src}
                src={src}
                alt="截图"
                className="w-full rounded-lg border object-cover"
                loading="lazy"
              />
            ))}
          </div>
        )}

        {pkg.long_description && (
          <Card>
            <CardHeader>
              <CardTitle>详细介绍</CardTitle>
            </CardHeader>
            <CardContent>
              <p className="whitespace-pre-wrap text-sm text-muted-foreground">
                {pkg.long_description}
              </p>
            </CardContent>
          </Card>
        )}

        <Card>
          <CardHeader>
            <CardTitle>版本发布</CardTitle>
          </CardHeader>
          <CardContent className="space-y-3">
            {releases.length === 0 && (
              <p className="text-sm text-muted-foreground">暂无发布。</p>
            )}
            {releases.map((r) => (
              <div key={r.id}>
                <div className="flex items-center justify-between gap-4">
                  <div>
                    <p className="font-medium">{r.title}</p>
                    <p className="text-xs text-muted-foreground">
                      {new Date(r.release_date).toLocaleDateString("zh-CN")} ·{" "}
                      {formatSize(r.size)}
                    </p>
                  </div>
                  <Button asChild size="sm" variant="outline">
                    <a
                      href={api.downloadUrl(author, name, r.id)}
                      download={downloadFileName(name, r.name)}
                    >
                      <Download className="mr-1" /> 下载
                    </a>
                  </Button>
                </div>
                <Separator className="mt-3" />
              </div>
            ))}
          </CardContent>
        </Card>

        <PackageReviews author={author} name={name} />

        <PackageThreads author={author} name={name} />
      </div>

      <div className="space-y-4">
        <PackageOwnerActions author={author} name={name} />

        <Card>
          <CardHeader>
            <CardTitle className="text-base">安装</CardTitle>
          </CardHeader>
          <CardContent className="space-y-3">
            {latest ? (
              <Button asChild className="w-full">
                <a
                  href={api.downloadUrl(author, name, latest.id)}
                  download={downloadFileName(name, latest.name)}
                >
                  <Download className="mr-1" /> 下载最新版
                </a>
              </Button>
            ) : (
              <p className="text-sm text-muted-foreground">暂无可下载版本</p>
            )}

            <div className="space-y-1 text-sm">
              <InfoRow label="类型" value={pkg.type} />
              {pkg.license && <InfoRow label="许可证" value={pkg.license} />}
              {typeof pkg.downloads === "number" && (
                <InfoRow label="下载量" value={pkg.downloads.toLocaleString()} />
              )}
              {typeof pkg.score === "number" && (
                <InfoRow
                  label="评分"
                  value={
                    <span className="inline-flex items-center gap-1">
                      <Star className="h-3.5 w-3.5" />
                      {pkg.score.toFixed(1)}
                    </span>
                  }
                />
              )}
            </div>

            {(pkg.repo || pkg.website || pkg.forum_url) && (
              <>
                <Separator />
                <div className="space-y-2 text-sm">
                  {pkg.repo && <LinkRow href={pkg.repo} label="源码仓库" />}
                  {pkg.website && <LinkRow href={pkg.website} label="官网" />}
                  {pkg.forum_url && <LinkRow href={pkg.forum_url} label="论坛帖" />}
                </div>
              </>
            )}
          </CardContent>
        </Card>

        {pkg.tags && pkg.tags.length > 0 && (
          <Card>
            <CardHeader>
              <CardTitle className="text-base">标签</CardTitle>
            </CardHeader>
            <CardContent className="flex flex-wrap gap-2">
              {pkg.tags.map((t) => (
                <Badge key={t} variant="outline">
                  {t}
                </Badge>
              ))}
            </CardContent>
          </Card>
        )}
      </div>
    </div>
  );
}

function InfoRow({ label, value }: { label: string; value: React.ReactNode }) {
  return (
    <div className="flex items-center justify-between">
      <span className="text-muted-foreground">{label}</span>
      <span className="font-medium">{value}</span>
    </div>
  );
}

function LinkRow({ href, label }: { href: string; label: string }) {
  return (
    <a
      href={href}
      target="_blank"
      rel="noopener noreferrer"
      className="flex items-center gap-1 text-primary hover:underline"
    >
      <ExternalLink className="h-3.5 w-3.5" /> {label}
    </a>
  );
}
