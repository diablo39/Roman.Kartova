import { useAdminSession } from "../api/useAdminSession";
import { orDash } from "@/lib/utils/format";

export function AdminLandingPage() {
  const { data } = useAdminSession();
  return (
    <div className="max-w-2xl space-y-2">
      <h1 className="text-2xl font-semibold text-primary">Platform Admin</h1>
      <p className="text-sm text-secondary">
        Signed in as {orDash(data?.displayName)} · {orDash(data?.email)}
      </p>
      <p className="text-sm text-tertiary">Organization management is coming.</p>
    </div>
  );
}
