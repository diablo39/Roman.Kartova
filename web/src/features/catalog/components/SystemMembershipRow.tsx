import { useState } from "react";
import { Link } from "react-router-dom";

import { Button } from "@/components/base/buttons/button";
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
 */
export function SystemMembershipRow({ componentKind, componentId, componentDisplayName, componentTeamId }: Props) {
  const [dialogOpen, setDialogOpen] = useState(false);
  const { hasPermission, role, teamIds } = usePermissions();
  const membership = useComponentSystem(componentKind, componentId);

  const canManage =
    hasPermission(KartovaPermissions.CatalogRelationshipsWrite) &&
    (role === "OrgAdmin" || teamIds.includes(componentTeamId));

  return (
    <>
      <hr className="border-secondary" />
      <section className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <div className="text-xs uppercase tracking-wide text-tertiary">System</div>
          <div className="mt-1 text-sm">
            {membership.systemId ? (
              <Link to={entityDetailPath("system", membership.systemId)} className="text-primary hover:underline">
                {membership.systemDisplayName ?? "View system"}
              </Link>
            ) : (
              <span className="italic text-tertiary">Not assigned</span>
            )}
          </div>
        </div>
        {canManage && (
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
          currentSystemName={membership.systemDisplayName}
        />
      )}
    </>
  );
}
