import { useAdminSession } from "../api/useAdminSession";

// An operator record may lack a name or email (KC profile gaps); never render a blank.
const orDash = (value: string | undefined) => (value && value.trim() ? value : "—");

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
