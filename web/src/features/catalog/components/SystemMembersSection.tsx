import { useState } from "react";
import { Link } from "react-router-dom";
import { toast } from "sonner";
import { Badge } from "@/components/base/badges/badges";
import { Button } from "@/components/base/buttons/button";
import { Table } from "@/components/application/table/table";
import { TableSkeleton, TablePager } from "@/components/application/data-table/data-table";
import { usePermissions } from "@/shared/auth/usePermissions";
import { KartovaPermissions } from "@/shared/auth/permissions";
import { useRelationshipsList } from "@/features/catalog/api/relationships";
import { useSetComponentSystem, type ComponentKind } from "@/features/catalog/api/systems";
import { entityDetailPath, ENTITY_KIND_LABEL } from "@/features/catalog/relationships/graphModel";
import { isRelationshipKind } from "@/features/catalog/relationships/relationshipTypeRules";
import { AddSystemMemberDialog } from "@/features/catalog/components/AddSystemMemberDialog";
import { toastProblem } from "@/shared/forms/toastProblem";

interface Props {
  systemId: string;
  systemTeamId: string;
  systemDisplayName: string;
}

// Only Application/Service can be PartOf a System (ADR-0111 amended) — Api never is, even
// though the read path applies no type filter server-side (see the module docblock below).
// Narrow before offering Remove so a client-side-visible drift edge never drives the setter
// with a kind it does not accept.
function asComponentKind(kind: string): ComponentKind | null {
  return kind === "application" || kind === "service" ? kind : null;
}

// Members are the components PARTOF this System — the SOURCE side of every incoming
// edge. Write-time rules (RelationshipTypeRules.IsAllowedPair) restrict incoming-to-System
// edges to PartOf, but the READ path (ListRelationshipsForEntityHandler) applies no type
// filter — so we filter to `partOf` client-side to stay drift-tolerant against backfills /
// direct DB writes (ADR-0111 amendment hedge; cf. e2e/tests/relationship-drift.spec.ts).
export function SystemMembersSection({ systemId, systemTeamId, systemDisplayName }: Props) {
  const [dialogOpen, setDialogOpen] = useState(false);
  const { hasPermission, role, teamIds } = usePermissions();
  const canManage =
    hasPermission(KartovaPermissions.CatalogRelationshipsWrite) &&
    (role === "OrgAdmin" || teamIds.includes(systemTeamId));

  const members = useRelationshipsList({ entityKind: "system", entityId: systemId, direction: "incoming" });
  const rows = members.items.filter((r) => r.type === "partOf");
  const setMembership = useSetComponentSystem();

  // Never offer Assign while the members list itself is unknown (mirrors
  // SystemMembershipRow's rule): presenting an unloaded/errored read as "nothing assigned"
  // alongside an inviting action risks the user assigning against stale/failed state.
  const showAssign = canManage && !members.isLoading && !members.isError;

  const onRemove = async (componentKind: ComponentKind, componentId: string, componentDisplayName: string) => {
    if (!window.confirm(`Remove ${componentDisplayName} from ${systemDisplayName}?`)) return;
    try {
      await setMembership.mutateAsync({ componentKind, componentId, systemId: null });
      toast.success(`${componentDisplayName} removed from ${systemDisplayName}.`);
    } catch (err) {
      // Same dispatch table as AddSystemMemberDialog/AssignSystemDialog: this Remove action
      // drives the identical useSetComponentSystem mutation, so a concurrent-move race or a
      // permission failure deserves the same specific message rather than one undifferentiated
      // string that discards every failure mode (409/403/422/network alike).
      toastProblem(err, {
        byProblemType: {
          "component-already-in-system":
            "Someone else just assigned this component to a System. Refresh and try again.",
        },
        byStatus: {
          403: "You can only move a component out of a System your team stewards.",
        },
        fallback: "Failed to remove the component.",
      });
    }
  };

  return (
    <section className="space-y-2" aria-label="Members">
      <div className="flex items-center justify-between">
        <h3 className="text-sm font-semibold text-primary">Members</h3>
        {showAssign && (
          <Button color="secondary" size="sm" onClick={() => setDialogOpen(true)}>
            Assign component
          </Button>
        )}
      </div>
      {members.isLoading ? (
        <Table aria-label="Members">
          <Table.Header>
            <Table.Head id="entity" isRowHeader>
              Component
            </Table.Head>
            <Table.Head id="kind">Kind</Table.Head>
            {canManage && <Table.Head id="actions"> </Table.Head>}
          </Table.Header>
          <TableSkeleton rows={3} cells={canManage ? 3 : 2} />
        </Table>
      ) : members.isError ? (
        <p className="text-sm text-error-primary">Couldn&apos;t load members.</p>
      ) : rows.length === 0 ? (
        <p className="text-sm italic text-tertiary">No components assigned yet.</p>
      ) : (
        <>
          <Table aria-label="Members">
            <Table.Header>
              <Table.Head id="entity" isRowHeader>
                Component
              </Table.Head>
              <Table.Head id="kind">Kind</Table.Head>
              {canManage && <Table.Head id="actions"> </Table.Head>}
            </Table.Header>
            <Table.Body>
              {rows.map((r) => {
                const m = r.source;
                const componentKind = asComponentKind(m.kind);
                return (
                  <Table.Row key={r.id} id={r.id}>
                    <Table.Cell>
                      {isRelationshipKind(m.kind) ? (
                        <Link
                          to={entityDetailPath(m.kind, m.id)}
                          className="text-primary hover:underline"
                        >
                          {m.displayName}
                        </Link>
                      ) : (
                        <span className="text-primary">{m.displayName}</span>
                      )}
                    </Table.Cell>
                    <Table.Cell>
                      <Badge type="pill-color" size="sm" color="gray">
                        {ENTITY_KIND_LABEL[m.kind] ?? m.kind}
                      </Badge>
                    </Table.Cell>
                    {canManage && (
                      <Table.Cell>
                        {componentKind && (
                          <Button
                            color="secondary-destructive"
                            size="sm"
                            isDisabled={setMembership.isPending}
                            onClick={() => onRemove(componentKind, m.id, m.displayName)}
                          >
                            Remove
                          </Button>
                        )}
                      </Table.Cell>
                    )}
                  </Table.Row>
                );
              })}
            </Table.Body>
          </Table>
          <TablePager
            hasPrev={members.hasPrev}
            hasNext={members.hasNext}
            onPrev={members.goPrev}
            onNext={members.goNext}
            pageSize={rows.length}
          />
        </>
      )}

      {dialogOpen && (
        <AddSystemMemberDialog
          open
          onOpenChange={setDialogOpen}
          system={{ id: systemId, displayName: systemDisplayName }}
        />
      )}
    </section>
  );
}
