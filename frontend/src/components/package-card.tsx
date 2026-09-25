import Link from "next/link";
import { Card, CardContent, CardFooter } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { ImageOff } from "lucide-react";
import type { PackageShort } from "@/lib/api";

const TYPE_LABEL: Record<string, string> = {
  mod: "Mod",
  game: "子游戏",
  txp: "材质包",
};

export function PackageCard({ pkg }: { pkg: PackageShort }) {
  return (
    <Link
      href={`/packages/${pkg.author}/${pkg.name}`}
      className="group block focus-visible:outline-none"
    >
      <Card className="h-full overflow-hidden pt-0 transition-all duration-200 group-hover:-translate-y-0.5 group-hover:shadow-lg group-focus-visible:ring-2 group-focus-visible:ring-ring">
        <div className="relative aspect-video w-full overflow-hidden bg-muted">
          {pkg.thumbnail ? (
            // eslint-disable-next-line @next/next/no-img-element
            <img
              src={pkg.thumbnail}
              alt={pkg.title}
              className="h-full w-full object-cover transition-transform duration-300 group-hover:scale-105"
              loading="lazy"
            />
          ) : (
            <div className="flex h-full w-full items-center justify-center text-muted-foreground">
              <ImageOff className="size-8 opacity-40" />
            </div>
          )}
          <div className="absolute right-2 top-2 flex gap-1">
            {pkg.origin === "upstream" ? (
              <Badge variant="secondary" className="backdrop-blur">
                {pkg.source_name ?? "上游"}
              </Badge>
            ) : (
              <Badge className="backdrop-blur">{pkg.source_name ?? "本站"}</Badge>
            )}
            {pkg.featured && <Badge variant="destructive" className="backdrop-blur">精选</Badge>}
          </div>
        </div>

        <CardContent className="space-y-1.5">
          <div className="flex items-start justify-between gap-2">
            <h3 className="line-clamp-1 font-semibold leading-tight tracking-tight">
              {pkg.title}
            </h3>
          </div>
          <p className="text-xs text-muted-foreground">by {pkg.author}</p>
          <p className="line-clamp-2 min-h-[2.5rem] text-sm text-muted-foreground">
            {pkg.short_description}
          </p>
        </CardContent>

        <CardFooter>
          <Badge variant="outline" className="font-normal">
            {TYPE_LABEL[pkg.type] ?? pkg.type}
          </Badge>
        </CardFooter>
      </Card>
    </Link>
  );
}
