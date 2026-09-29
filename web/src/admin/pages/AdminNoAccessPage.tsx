import { Button } from "@/components/base/buttons/button";
import { CenteredMessage } from "@/components/layout/CenteredMessage";

/** Terminal 403 surface: authenticated in kartova-platform, but not a platform operator. */
export function AdminNoAccessPage({ onSignOut }: { onSignOut: () => void }) {
  return (
    <CenteredMessage
      heading="No access"
      body="This account is not a platform operator."
      action={
        <Button color="secondary" size="md" onClick={onSignOut}>
          Sign out
        </Button>
      }
    />
  );
}
