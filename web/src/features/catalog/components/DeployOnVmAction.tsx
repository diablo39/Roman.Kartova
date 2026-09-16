import { useState } from "react";

import { Button } from "@/components/base/buttons/button";
import { DeployOnVmDialog } from "@/features/catalog/components/DeployOnVmDialog";

interface Props {
  kind: "application" | "service";
  id: string;
  displayName: string;
  /** Renders nothing when false — caller owns the gating decision (permission + ownership). */
  canDeploy: boolean;
}

/**
 * "Deploy on VM" action: button + its own open-state + the DeployOnVmDialog it triggers.
 * Extracted from ApplicationDetailPage/ServiceDetailPage, which duplicated this block
 * near-verbatim (/simplify, catalog-vm-linking). Scalars in, not a `component` object, so
 * callers don't need to memoize a prop object per render.
 */
export function DeployOnVmAction({ kind, id, displayName, canDeploy }: Props) {
  const [open, setOpen] = useState(false);

  if (!canDeploy) return null;

  return (
    <>
      <hr className="border-secondary" />
      <div className="flex justify-end">
        <Button color="secondary" size="sm" onClick={() => setOpen(true)}>
          Deploy on VM
        </Button>
      </div>
      {open && (
        <DeployOnVmDialog open onOpenChange={setOpen} component={{ kind, id, displayName }} />
      )}
    </>
  );
}
