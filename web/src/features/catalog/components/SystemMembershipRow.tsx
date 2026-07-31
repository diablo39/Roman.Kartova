import { useState } from "react";
import { Link } from "react-router-dom";

import { Button } from "@/components/base/buttons/button";
import { Skeleton } from "@/components/base/skeleton/skeleton";
import { usePermissions } from "@/shared/auth/usePermissions";
import { KartovaPermissions } from "@/shared/auth/permissions";
import { useComponentSystem, type ComponentKind } from "@/features/catalog/api/systems";
import { entityDetailPath } from "@/features/catalog/relationships/graphModel";
import { AssignSystemDialog } from "@/features/catalog/components/AssignSystemDialog";

interface Props {
  componentKind: ComponentKind;
  componentId: string;
  componentDisplayName: string;
  componentTeamId: string;
}

/**
 * The component's System membership on its Overview tab: the System it belongs to (at most
 * one, ADR-0111 amended) plus Assign/Change. Removal lives inside the dialog so this row
 * stays a single action. Gated like every relationship write — the permission plus
 * OrgAdmin-or-own-team (the System side of the edge is also authorized server-side, ADR-0108,
 * so a System steward can be authorized to act yet still see no button here — accepted,
 * documented asymmetry).
 *
 * `isLoading`/`isError` are branched explicitly (mirroring `RelationshipsSection.tsx`'s
 * loading/error handling) rather than falling through to "Not assigned": collapsing an
 * unloaded or failed read into "Not assigned" would let a user click Assign against a
 * component that may already belong to a System — `AssignSystemDialog`'s atomic PUT would
 * then silently replace the real membership with `currentSystemName: null` in view, no
 * "you're overwriting X" warning shown anywhere.
 */
export function SystemMembershipRow({ componentKind, componentId, componentDisplayName, componentTeamId }: Props) {
  const [dialogOpen, setDialogOpen] = useState(false);
  const { hasPermission, role, teamIds } = usePermissions();
  const membership = useComponentSystem(componentKind, componentId);

  const canManage =
    hasPermission(KartovaPermissions.CatalogRelationshipsWrite) &&
    (role === "OrgAdmin" || teamIds.includes(componentTeamId));
  // Never offer the action while the membership itself is unknown — see docblock.
  const showAction = canManage && !membership.isLoading && !membership.isError;

  return (
    <>
      <hr className="border-secondary" />
      <section className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <div className="text-xs uppercase tracking-wide text-tertiary">System</div>
          <div className="mt-1 text-sm">
            {membership.isLoading ? (
              <Skeleton className="h-4 w-32" />
            ) : membership.isError ? (
              <span className="text-error-primary">Couldn&apos;t load System membership.</span>
            ) : membership.systemId ? (
              <Link to={entityDetailPath("system", membership.systemId)} className="text-primary hover:underline">
                {membership.systemDisplayName ?? "View system"}
              </Link>
            ) : (
              <span className="italic text-tertiary">Not assigned</span>
            )}
          </div>
        </div>
        {showAction && (
          <Button color="secondary" size="sm" onClick={() => setDialogOpen(true)}>
            {membership.systemId ? "Change" : "Assign"}
          </Button>
        )}
      </section>

      {dialogOpen && (
        <AssignSystemDialog
          open
          onOpenChange={setDialogOpen}
          component={{ kind: componentKind, id: componentId, displayName: componentDisplayName }}
          currentSystemId={membership.systemId}
          currentSystemName={membership.systemDisplayName}
        />
      )}
    </>
  );
}
