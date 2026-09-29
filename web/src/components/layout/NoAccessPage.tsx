import { CenteredMessage } from "./CenteredMessage";

export function NoAccessPage() {
  return (
    <CenteredMessage
      heading="No access"
      body="You don't have access to this organization. Contact your organization admin."
    />
  );
}
