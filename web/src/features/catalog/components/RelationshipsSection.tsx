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
import { relationshipTypeLabel, type RelationshipKind, type CreatableRelationshipType } from "@/features/catalog/relationships/relationshipTypeRules";
import { AddRelationshipDialog } from "@/features/catalog/components/AddRelationshipDialog";
import type { FixedRole } from "@/features/catalog/relationships/relationshipTypeRules";
import { Tooltip, TooltipTrigger } from "@/components/base/tooltip/tooltip";
import { HelpCircle } from "@untitledui/icons";

interface Props {
  entityKind: RelationshipKind;
  entityId: string;
  entityTeamId: string;
  entityDisplayName: string;
}

function entityLink(kind: string, id: string) {
  return `/catalog/${kind === "application" ? "applications" : "services"}/${id}`;
}

const relationshipOriginLabel: Record<string, string> = { manual: "Manual", scan: "Scan", agent: "Agent" };

export function RelationshipsSection({ entityKind, entityId, entityTeamId, entityDisplayName }: Props) {
  const { hasPermission, role, teamIds } = usePermissions();
  const canManage =
    hasPermission(KartovaPermissions.CatalogRelationshipsWrite) &&
    (role === "OrgAdmin" || teamIds.includes(entityTeamId));

  const outgoing = useRelationshipsList({ entityKind, entityId, direction: "outgoing" });
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
  ) => (
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
        {canManage && (
          <Button color="secondary" size="sm" onClick={() => setDialog(addRole)}>
            {addLabel}
          </Button>
        )}
      </div>
      {list.isLoading ? (
        <Table aria-label={title}>
          <Table.Header>
            <Table.Head id="type">Type</Table.Head>
            <Table.Head id="entity">Entity</Table.Head>
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
              <Table.Head id="entity">Entity</Table.Head>
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
                      <CreatedByLink user={null} />
                    </Table.Cell>
                    {canManage && (
                      <Table.Cell>
                        <Button
                          color="tertiary"
                          size="sm"
                          onClick={() => onDelete(r.id)}
                          isDisabled={del.isPending}
                        >
                          Delete
                        </Button>
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

  return (
    <section className="space-y-6" aria-label="Relationships">
      {renderGroup(
        "Dependencies",
        {
          title: "Dependencies (outgoing)",
          description: `What this ${entityKind} depends on — the things it needs to work. If a dependency breaks, this ${entityKind} may be affected.`,
        },
        "No dependencies.",
        outgoing,
        (r) => r.target,
        "source",
        "Add dependency",
      )}
      {renderGroup(
        "Dependents",
        {
          title: "Dependents (incoming)",
          description: `What depends on this ${entityKind} — its consumers. If this ${entityKind} breaks, these may be affected.`,
        },
        `Nothing depends on this ${entityKind.toLowerCase()}.`,
        incoming,
        (r) => r.source,
        "target",
        "Add dependent",
      )}
      {dialog && (
        <AddRelationshipDialog
          open
          onOpenChange={(o) => {
            if (!o) setDialog(null);
          }}
          fixedRole={dialog}
          fixedEntity={fixedEntity}
        />
      )}
    </section>
  );
}
