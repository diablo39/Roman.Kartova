import { Link } from "react-router-dom";
import { Badge } from "@/components/base/badges/badges";
import { Table } from "@/components/application/table/table";
import { Skeleton } from "@/components/base/skeleton/skeleton";
import { useApiSurface, type ApiSurfaceItem } from "@/features/catalog/api/apiSurface";

// Wire values are camelCase per ADR-0109.
const STYLE_LABEL: Record<string, string> = {
  rest: "REST",
  grpc: "gRPC",
  graphQL: "GraphQL",
  asyncApi: "AsyncAPI",
};

const STYLE_ORDER: Record<string, number> = { rest: 0, grpc: 1, graphQL: 2, asyncApi: 3 };

function sortItems(items: ApiSurfaceItem[]): ApiSurfaceItem[] {
  return [...items].sort(
    (a, b) =>
      (STYLE_ORDER[a.style] ?? 99) - (STYLE_ORDER[b.style] ?? 99) ||
      a.displayName.localeCompare(b.displayName),
  );
}

interface Props {
  entityKind: "service" | "application";
  entityId: string;
}

export function ApiSurfaceSection({ entityKind, entityId }: Props) {
  const query = useApiSurface(entityKind, entityId);

  if (query.isLoading) return <Skeleton className="h-40 w-full" />;
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
      />
      <ApiTable
        title="Consumes"
        emptyCopy="No APIs consumed."
        items={sortItems(consumes)}
        showOrigin={false}
      />
    </section>
  );
}

function ApiTable({
  title,
  emptyCopy,
  items,
  showOrigin,
}: {
  title: string;
  emptyCopy: string;
  items: ApiSurfaceItem[];
  showOrigin: boolean;
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
                      {STYLE_LABEL[i.style] ?? i.style}
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
                </Table.Row>
              ))}
            </Table.Body>
          </Table>
        </div>
      )}
    </div>
  );
}
