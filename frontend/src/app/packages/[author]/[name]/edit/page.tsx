import { notFound } from "next/navigation";
import { api } from "@/lib/api";
import { PackageForm } from "@/components/package-form";

export const dynamic = "force-dynamic";

export default async function EditPackagePage({
  params,
}: {
  params: Promise<{ author: string; name: string }>;
}) {
  const { author, name } = await params;

  let pkg;
  try {
    pkg = await api.getPackage(author, name);
  } catch {
    notFound();
  }

  return <PackageForm mode="edit" author={author} name={name} existing={pkg} />;
}
