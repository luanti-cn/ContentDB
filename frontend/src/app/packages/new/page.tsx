import { PackageForm } from "@/components/package-form";

export const metadata = { title: "创建新包" };

export default function NewPackagePage() {
  return <PackageForm mode="create" />;
}
