/** Shared centered-card layout for terminal/error states (no access, session error, sign-in failed, …). */
export function CenteredMessage({
  heading,
  body,
  action,
}: {
  heading: string;
  body: React.ReactNode;
  action?: React.ReactNode;
}) {
  return (
    <div className="flex h-full items-center justify-center">
      <div className="max-w-md space-y-3 text-center">
        <h1 className="text-2xl font-semibold text-primary">{heading}</h1>
        <p className="text-sm text-tertiary">{body}</p>
        {action}
      </div>
    </div>
  );
}
