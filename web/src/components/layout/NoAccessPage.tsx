export function NoAccessPage() {
  return (
    <div className="flex h-full items-center justify-center">
      <div className="max-w-md space-y-3 text-center">
        <h1 className="text-2xl font-semibold text-primary">No access</h1>
        <p className="text-sm text-tertiary">
          You don&apos;t have access to this organization. Contact your organization admin.
        </p>
      </div>
    </div>
  );
}
