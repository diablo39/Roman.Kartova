import { Link } from "react-router-dom";
import { toast } from "sonner";
import { Badge } from "@/components/base/badges/badges";
import { Button } from "@/components/base/buttons/button";
import { Table } from "@/components/application/table/table";
import { TableSkeleton } from "@/components/application/data-table/data-table";
import { useApiSurface, type ApiSurfaceItem } from "@/features/catalog/api/apiSurface";
import { useDeleteRelationship } from "@/features/catalog/api/relationships";
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
}

export function ApiSurfaceSection({ entityKind, entityId, entityTeamId }: Props) {
  const query = useApiSurface(entityKind, entityId);
  const { hasPermission, role, teamIds } = usePermissions();
  const canManage =
    hasPermission(KartovaPermissions.CatalogRelationshipsWrite) &&
    (role === "OrgAdmin" || teamIds.includes(entityTeamId));
  const del = useDeleteRelationship();

  const onRemove = async (relationshipId: string) => {
    if (!window.confirm("Remove this API relationship?")) return;
    try {
      await del.mutateAsync(relationshipId);
      toast.success("API relationship removed");
    } catch {
      toast.error("Failed to remove API relationship");
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
      />
      <ApiTable
        title="Consumes"
        emptyCopy="No APIs consumed."
        items={sortItems(consumes)}
        showOrigin={false}
        canManage={canManage}
        onRemove={onRemove}
        isRemoving={del.isPending}
      />
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
}: {
  title: string;
  emptyCopy: string;
  items: ApiSurfaceItem[];
  showOrigin: boolean;
  canManage: boolean;
  onRemove: (relationshipId: string) => void;
  isRemoving: boolean;
}) {
  return (
    <div className="space-y-2">
      <h3 className="text-sm font-semibold text-primary">{title}</h3>
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
                          color="tertiary"
                          size="sm"
                          onClick={() => onRemove(i.relationshipId!)}
                          isDisabled={isRemoving}
                        >
                          Remove
                        </Button>
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
