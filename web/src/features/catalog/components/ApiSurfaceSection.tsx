import { useState } from "react";
import { Link } from "react-router-dom";
import { toast } from "sonner";
import { Badge } from "@/components/base/badges/badges";
import { Button } from "@/components/base/buttons/button";
import { Trash01 } from "@untitledui/icons";
import { Table } from "@/components/application/table/table";
import { TableSkeleton } from "@/components/application/data-table/data-table";
import { useApiSurface, type ApiSurfaceItem } from "@/features/catalog/api/apiSurface";
import { useDeleteRelationship } from "@/features/catalog/api/relationships";
import { AddRelationshipDialog } from "@/features/catalog/components/AddRelationshipDialog";
import { usePermissions } from "@/shared/auth/usePermissions";
import { KartovaPermissions } from "@/shared/auth/permissions";
import { API_STYLE_LABEL, API_STYLES } from "@/features/catalog/schemas/registerApi";

function styleOrder(style: string): number {
  const index = API_STYLES.indexOf(style as (typeof API_STYLES)[number]);
  return index === -1 ? 99 : index;
}

function sortItems(items: ApiSurfaceItem[]): ApiSurfaceItem[] {
  return [...items].sort(
    (a, b) => styleOrder(a.style) - styleOrder(b.style) || a.displayName.localeCompare(b.displayName),
  );
}

function ApiSurfaceLoadingSkeleton({ canManage }: { canManage: boolean }) {
  return (
    <section className="space-y-6" aria-label="APIs">
      {[
        { title: "Provides", cells: 5 },
        { title: "Consumes", cells: 4 },
      ].map(({ title, cells }) => (
        <div className="space-y-2" key={title}>
          <h3 className="text-sm font-semibold text-primary">{title}</h3>
          <Table aria-label={title}>
            <Table.Header>
              <Table.Head id="name" isRowHeader>
                Name
              </Table.Head>
              <Table.Head id="style">Style</Table.Head>
              <Table.Head id="version">Version</Table.Head>
              <Table.Head id="spec">Spec</Table.Head>
              {cells === 5 && <Table.Head id="origin">Origin</Table.Head>}
              {canManage && <Table.Head id="actions"> </Table.Head>}
            </Table.Header>
            <TableSkeleton rows={2} cells={cells + (canManage ? 1 : 0)} />
          </Table>
        </div>
      ))}
    </section>
  );
}

interface Props {
  entityKind: "service" | "application";
  entityId: string;
  entityTeamId: string;
  entityDisplayName: string;
}

export function ApiSurfaceSection({ entityKind, entityId, entityTeamId, entityDisplayName }: Props) {
  const query = useApiSurface(entityKind, entityId);
  const { hasPermission, role, teamIds } = usePermissions();
  const canManage =
    hasPermission(KartovaPermissions.CatalogRelationshipsWrite) &&
    (role === "OrgAdmin" || teamIds.includes(entityTeamId));
  const del = useDeleteRelationship();
  const [addType, setAddType] = useState<null | "providesApiFor" | "consumesApiFrom">(null);

  const onRemove = async (relationshipId: string) => {
    if (!window.confirm("Delete this relationship?")) return;
    try {
      await del.mutateAsync(relationshipId);
      toast.success("Relationship removed");
    } catch {
      toast.error("Failed to remove relationship");
    }
  };

  if (query.isLoading) return <ApiSurfaceLoadingSkeleton canManage={canManage} />;
  if (query.isError || !query.data)
    return <p className="text-sm text-error-primary">Couldn&apos;t load APIs.</p>;

  const { provides, consumes } = query.data;

  return (
    <section className="space-y-6" aria-label="APIs">
      <ApiTable
        title="Provides"
        emptyCopy="No APIs provided."
        items={sortItems(provides)}
        showOrigin
        canManage={canManage}
        onRemove={onRemove}
        isRemoving={del.isPending}
        onAdd={canManage ? () => setAddType("providesApiFor") : undefined}
      />
      <ApiTable
        title="Consumes"
        emptyCopy="No APIs consumed."
        items={sortItems(consumes)}
        showOrigin={false}
        canManage={canManage}
        onRemove={onRemove}
        isRemoving={del.isPending}
        onAdd={canManage ? () => setAddType("consumesApiFrom") : undefined}
      />
      {addType && (
        <AddRelationshipDialog
          open
          onOpenChange={(o) => {
            if (!o) setAddType(null);
          }}
          fixedRole="source"
          fixedEntity={{ kind: entityKind, id: entityId, displayName: entityDisplayName }}
          restrictTypes={[addType]}
          heading={addType === "providesApiFor" ? "Add provided API" : "Add consumed API"}
        />
      )}
    </section>
  );
}

function ApiTable({
  title,
  emptyCopy,
  items,
  showOrigin,
  canManage,
  onRemove,
  isRemoving,
  onAdd,
}: {
  title: string;
  emptyCopy: string;
  items: ApiSurfaceItem[];
  showOrigin: boolean;
  canManage: boolean;
  onRemove: (relationshipId: string) => void;
  isRemoving: boolean;
  onAdd?: () => void;
}) {
  return (
    <div className="space-y-2">
      <div className="flex items-center justify-between">
        <h3 className="text-sm font-semibold text-primary">{title}</h3>
        {onAdd && (
          <Button color="secondary" size="sm" onClick={onAdd}>
            {title === "Provides" ? "Add provided API" : "Add consumed API"}
          </Button>
        )}
      </div>
      {items.length === 0 ? (
        <p className="text-sm italic text-tertiary">{emptyCopy}</p>
      ) : (
        <div className="overflow-hidden rounded-lg ring-1 ring-secondary">
          <Table aria-label={title}>
            <Table.Header>
              <Table.Head id="name" isRowHeader>
                Name
              </Table.Head>
              <Table.Head id="style">Style</Table.Head>
              <Table.Head id="version">Version</Table.Head>
              <Table.Head id="spec">Spec</Table.Head>
              {showOrigin && <Table.Head id="origin">Origin</Table.Head>}
              {canManage && <Table.Head id="actions"> </Table.Head>}
            </Table.Header>
            <Table.Body>
              {items.map((i) => (
                <Table.Row key={i.apiId} id={i.apiId}>
                  <Table.Cell>
                    <Link to={`/catalog/apis/${i.apiId}`} className="text-primary hover:underline">
                      {i.displayName}
                    </Link>
                  </Table.Cell>
                  <Table.Cell>
                    <Badge type="pill-color" size="sm" color="brand">
                      {API_STYLE_LABEL[i.style as keyof typeof API_STYLE_LABEL] ?? i.style}
                    </Badge>
                  </Table.Cell>
                  <Table.Cell className="font-mono text-sm">{i.version}</Table.Cell>
                  <Table.Cell>
                    {i.hasSpec ? (
                      <Badge type="pill-color" size="sm" color="success">
                        Spec
                      </Badge>
                    ) : (
                      <span className="text-sm text-tertiary">—</span>
                    )}
                  </Table.Cell>
                  {showOrigin && (
                    <Table.Cell className="text-sm">
                      {i.origin === "derived" && i.viaApplicationId ? (
                        <span className="text-tertiary">
                          Derived · via{" "}
                          <Link
                            to={`/catalog/applications/${i.viaApplicationId}`}
                            className="text-primary hover:underline"
                          >
                            {i.viaApplicationDisplayName ?? "application"}
                          </Link>
                        </span>
                      ) : (
                        <span className="text-tertiary">Direct</span>
                      )}
                    </Table.Cell>
                  )}
                  {canManage && (
                    <Table.Cell>
                      {i.relationshipId ? (
                        <Button
                          color="primary-destructive"
                          size="sm"
                          iconLeading={Trash01}
                          aria-label="Delete"
                          onClick={() => onRemove(i.relationshipId!)}
                          isDisabled={isRemoving}
                        />
                      ) : null}
                    </Table.Cell>
                  )}
                </Table.Row>
              ))}
            </Table.Body>
          </Table>
        </div>
      )}
    </div>
  );
}
