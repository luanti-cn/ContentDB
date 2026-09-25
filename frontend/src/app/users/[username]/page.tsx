import Link from "next/link";
import { api } from "@/lib/api";
import { PackageCard } from "@/components/package-card";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { ExternalLink, User as UserIcon } from "lucide-react";

export const dynamic = "force-dynamic";

export default async function UserPage({
  params,
}: {
  params: Promise<{ username: string }>;
}) {
  const { username } = await params;

  const [profile, packages] = await Promise.all([
    api.getUser(username).catch(() => null),
    api.listPackages({ author: username }).catch(() => []),
  ]);

  const displayName = profile?.display_name || username;

  return (
    <div className="space-y-8">
      <div className="flex items-start gap-4">
        <div className="flex size-14 items-center justify-center rounded-full bg-muted">
          <UserIcon className="size-7 text-muted-foreground" />
        </div>
        <div className="space-y-1">
          <div className="flex items-center gap-2">
            <h1 className="text-2xl font-bold tracking-tight">{displayName}</h1>
            {profile?.rank && <Badge variant="outline">{profile.rank}</Badge>}
          </div>
          <p className="text-sm text-muted-foreground">@{username}</p>
        </div>
      </div>

      {profile &&
        (profile.website_url ||
          profile.donate_url ||
          profile.github_username ||
          profile.forums_username) && (
          <Card>
            <CardHeader>
              <CardTitle className="text-base">链接</CardTitle>
            </CardHeader>
            <CardContent className="space-y-2 text-sm">
              {profile.website_url && <LinkRow href={profile.website_url} label="个人网站" />}
              {profile.donate_url && <LinkRow href={profile.donate_url} label="捐赠" />}
              {profile.github_username && (
                <LinkRow
                  href={`https://github.com/${profile.github_username}`}
                  label={`GitHub: ${profile.github_username}`}
                />
              )}
              {profile.forums_username && (
                <p className="text-muted-foreground">论坛: {profile.forums_username}</p>
              )}
            </CardContent>
          </Card>
        )}

      <div className="space-y-4">
        <h2 className="text-lg font-semibold">
          发布的包{" "}
          <span className="text-sm font-normal text-muted-foreground">
            ({packages.length})
          </span>
        </h2>
        {packages.length === 0 ? (
          <p className="text-sm text-muted-foreground">
            该用户还没有已发布的包。
            <Link href="/explore" className="ml-1 text-primary hover:underline">
              去逛内容库
            </Link>
          </p>
        ) : (
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
            {packages.map((p) => (
              <PackageCard key={`${p.author}/${p.name}`} pkg={p} />
            ))}
          </div>
        )}
      </div>
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
