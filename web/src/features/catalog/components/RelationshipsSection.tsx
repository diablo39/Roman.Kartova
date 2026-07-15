import { useState } from "react";
import { Link } from "react-router-dom";
import { toast } from "sonner";
import { Badge } from "@/components/base/badges/badges";
import { Button } from "@/components/base/buttons/button";
import { Table } from "@/components/application/table/table";
import { TableSkeleton, TablePager } from "@/components/application/data-table/data-table";
import { CreatedByLink } from "@/features/users/components/CreatedByLink";
import { usePermissions } from "@/shared/auth/usePermissions";
import { KartovaPermissions } from "@/shared/auth/permissions";
import {
  useRelationshipsList,
  useDeleteRelationship,
  type RelationshipResponse,
} from "@/features/catalog/api/relationships";
import { relationshipTypeLabel, offerableTypes, type RelationshipKind, type CreatableRelationshipType } from "@/features/catalog/relationships/relationshipTypeRules";
import { entityDetailPath } from "@/features/catalog/relationships/graphModel";
import { AddRelationshipDialog } from "@/features/catalog/components/AddRelationshipDialog";
import type { FixedRole } from "@/features/catalog/relationships/relationshipTypeRules";
import { Tooltip, TooltipTrigger } from "@/components/base/tooltip/tooltip";
import { HelpCircle, Trash01 } from "@untitledui/icons";

interface Props {
  entityKind: RelationshipKind;
  entityId: string;
  entityTeamId: string;
  entityDisplayName: string;
  variant?: "full" | "incoming-only";
}

function entityLink(kind: string, id: string) {
  return entityDetailPath(kind as RelationshipKind, id);
}

const relationshipOriginLabel: Record<string, string> = { manual: "Manual", scan: "Scan", agent: "Agent" };

// Provides/consumes API edges are managed from the API-surface section (add + remove there),
// so the Relationships dialog offers only the non-API dependency types.
const RELATIONSHIP_DIALOG_TYPES: CreatableRelationshipType[] = ["dependsOn", "instanceOf"];

export function RelationshipsSection({ entityKind, entityId, entityTeamId, entityDisplayName, variant = "full" }: Props) {
  const { hasPermission, role, teamIds } = usePermissions();
  // `readOnly` (the API detail page's incoming-only view) suppresses the Outgoing group and the
  // Add affordance — but NOT delete: an edge can be removed from EITHER endpoint's team per
  // ADR-0108 (symmetric delete), so the target-side page can delete its incoming edges too.
  const readOnly = variant === "incoming-only";
  const canManage =
    hasPermission(KartovaPermissions.CatalogRelationshipsWrite) &&
    (role === "OrgAdmin" || teamIds.includes(entityTeamId));

  const outgoing = useRelationshipsList(
    { entityKind, entityId, direction: "outgoing", excludeApiEdges: variant === "full" },
    { enabled: variant === "full" },
  );
  const incoming = useRelationshipsList({ entityKind, entityId, direction: "incoming" });
  const del = useDeleteRelationship();
  const [dialog, setDialog] = useState<null | FixedRole>(null);

  const onDelete = async (id: string) => {
    if (!window.confirm("Delete this relationship?")) return;
    try {
      await del.mutateAsync(id);
      toast.success("Relationship removed");
    } catch {
      toast.error("Failed to remove relationship");
    }
  };

  const fixedEntity = { kind: entityKind, id: entityId, displayName: entityDisplayName };

  const renderGroup = (
    title: string,
    help: { title: string; description: string },
    emptyCopy: string,
    list: ReturnType<typeof useRelationshipsList>,
    related: (r: RelationshipResponse) => RelationshipResponse["source"],
    addRole: FixedRole,
    addLabel: string,
  ) => {
    const canAdd =
      canManage &&
      offerableTypes(addRole, entityKind).some((t) => RELATIONSHIP_DIALOG_TYPES.includes(t));
    return (
      <div className="space-y-2">
        <div className="flex items-center justify-between">
          <div className="flex items-center gap-1.5">
            <h3 className="text-sm font-semibold text-primary">{title}</h3>
            <Tooltip title={help.title} description={help.description} placement="top">
              <TooltipTrigger
                aria-label={`What does "${title}" mean?`}
                className="cursor-help text-fg-quaternary transition hover:text-fg-quaternary_hover"
              >
                <HelpCircle className="size-4" aria-hidden="true" />
              </TooltipTrigger>
            </Tooltip>
          </div>
          {canAdd && (
            <Button color="secondary" size="sm" onClick={() => setDialog(addRole)}>
              {addLabel}
            </Button>
          )}
        </div>
        {list.isLoading ? (
          <Table aria-label={title}>
            <Table.Header>
              <Table.Head id="type">Type</Table.Head>
              <Table.Head id="entity" isRowHeader>Entity</Table.Head>
              <Table.Head id="origin">Origin</Table.Head>
              <Table.Head id="createdBy">Added by</Table.Head>
              {canManage && <Table.Head id="actions"> </Table.Head>}
            </Table.Header>
            <TableSkeleton rows={2} cells={canManage ? 5 : 4} />
          </Table>
        ) : list.isError ? (
          <p className="text-sm text-error-primary">Couldn&apos;t load relationships.</p>
        ) : list.items.length === 0 ? (
          <p className="text-sm italic text-tertiary">{emptyCopy}</p>
        ) : (
          <>
            <Table aria-label={title}>
              <Table.Header>
                <Table.Head id="type">Type</Table.Head>
                <Table.Head id="entity" isRowHeader>Entity</Table.Head>
                <Table.Head id="origin">Origin</Table.Head>
                <Table.Head id="createdBy">Added by</Table.Head>
                {canManage && <Table.Head id="actions"> </Table.Head>}
              </Table.Header>
              <Table.Body>
                {list.items.map((r) => {
                  const e = related(r);
                  const label =
                    relationshipTypeLabel[r.type as CreatableRelationshipType] ?? r.type;
                  return (
                    <Table.Row key={r.id} id={r.id}>
                      <Table.Cell>
                        <Badge type="pill-color" size="sm" color="brand">
                          {label}
                        </Badge>
                      </Table.Cell>
                      <Table.Cell>
                        <Link to={entityLink(e.kind, e.id)} className="text-primary hover:underline">
                          {e.displayName}
                        </Link>
                      </Table.Cell>
                      <Table.Cell>
                        <Badge type="pill-color" size="sm" color="gray">
                          {relationshipOriginLabel[r.origin] ?? r.origin}
                        </Badge>
                      </Table.Cell>
                      <Table.Cell>
                        <CreatedByLink user={r.createdBy} />
                      </Table.Cell>
                      {canManage && (
                        <Table.Cell>
                          <Button
                            color="primary-destructive"
                            size="sm"
                            iconLeading={Trash01}
                            aria-label="Delete"
                            className="*:data-icon:text-white hover:*:data-icon:text-white"
                            onClick={() => onDelete(r.id)}
                            isDisabled={del.isPending}
                          />
                        </Table.Cell>
                      )}
                    </Table.Row>
                  );
                })}
              </Table.Body>
            </Table>
            <TablePager
              hasPrev={list.hasPrev}
              hasNext={list.hasNext}
              onPrev={list.goPrev}
              onNext={list.goNext}
              pageSize={list.items.length}
            />
          </>
        )}
      </div>
    );
  };

  return (
    <section className="space-y-6" aria-label="Relationships">
      {!readOnly &&
        renderGroup(
          "Outgoing",
          {
            title: "Outgoing relationships",
            description: `Edges where ${entityDisplayName} is the source — what this ${entityKind} depends on, provides, consumes, or is an instance of.`,
          },
          "No outgoing relationships.",
          outgoing,
          (r) => r.target,
          "source",
          "Add outgoing",
        )}
      {renderGroup(
        "Incoming",
        {
          title: "Incoming relationships",
          description: `Edges where ${entityDisplayName} is the target — dependents, providers, consumers.`,
        },
        `Nothing points to this ${entityKind === "api" ? "API" : entityKind}.`,
        incoming,
        (r) => r.source,
        "target",
        "Add incoming",
      )}
      {dialog && (
        <AddRelationshipDialog
          open
          onOpenChange={(o) => {
            if (!o) setDialog(null);
          }}
          fixedRole={dialog}
          fixedEntity={fixedEntity}
          restrictTypes={RELATIONSHIP_DIALOG_TYPES}
        />
      )}
    </section>
  );
}
