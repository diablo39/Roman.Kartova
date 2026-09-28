import { Button } from "@/components/base/buttons/button";

/** Terminal 403 surface: authenticated in kartova-platform, but not a platform operator. */
export function AdminNoAccessPage({ onSignOut }: { onSignOut: () => void }) {
  return (
    <div className="flex h-full items-center justify-center">
      <div className="max-w-md space-y-3 text-center">
        <h1 className="text-2xl font-semibold text-primary">No access</h1>
        <p className="text-sm text-tertiary">This account is not a platform operator.</p>
        <Button color="secondary" size="md" onClick={onSignOut}>
          Sign out
        </Button>
      </div>
    </div>
  );
}
