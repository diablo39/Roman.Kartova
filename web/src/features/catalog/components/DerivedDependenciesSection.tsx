import { Link } from "react-router-dom";
import { Badge } from "@/components/base/badges/badges";
import { Table } from "@/components/application/table/table";
import { TableSkeleton } from "@/components/application/data-table/data-table";
import { useDerivedDependencies, type DerivedDependencyItem } from "@/features/catalog/api/derivedDependencies";

interface Props {
  entityId: string;
}

export function DerivedDependenciesSection({ entityId }: Props) {
  const query = useDerivedDependencies(entityId);

  if (query.isLoading) return <DerivedDependenciesSkeleton />;
  if (query.isError || !query.data)
    return <p className="text-sm text-error-primary">Couldn&apos;t load derived dependencies.</p>;

  const { dependencies, dependents } = query.data;

  return (
    <section className="space-y-6" aria-label="Derived dependencies">
      <DerivedTable title="Dependencies" emptyCopy="No derived dependencies." items={dependencies} />
      <DerivedTable
        title="Dependents"
        emptyCopy="Nothing derives a dependency on this service."
        items={dependents}
      />
    </section>
  );
}

function DerivedTableHeader() {
  return (
    <Table.Header>
      <Table.Head id="service" isRowHeader>
        Service
      </Table.Head>
      <Table.Head id="kind">Kind</Table.Head>
      <Table.Head id="provenance">Via</Table.Head>
    </Table.Header>
  );
}

function DerivedTable({
  title,
  emptyCopy,
  items,
}: {
  title: string;
  emptyCopy: string;
  items: DerivedDependencyItem[];
}) {
  return (
    <div className="space-y-2">
      <h3 className="text-sm font-semibold text-primary">{title}</h3>
      {items.length === 0 ? (
        <p className="text-sm italic text-tertiary">{emptyCopy}</p>
      ) : (
        <div className="overflow-hidden rounded-lg ring-1 ring-secondary">
          <Table aria-label={title}>
            <DerivedTableHeader />
            <Table.Body>
              {items.map((i) => (
                <Table.Row key={i.serviceId} id={i.serviceId}>
                  <Table.Cell>
                    <Link to={`/catalog/services/${i.serviceId}`} className="text-primary hover:underline">
                      {i.displayName}
                    </Link>
                  </Table.Cell>
                  <Table.Cell>
                    <Badge type="pill-color" size="sm" color="gray">
                      Derived
                    </Badge>
                  </Table.Cell>
                  <Table.Cell className="text-sm text-tertiary">
                    <ul className="space-y-0.5">
                      {i.paths.map((p) => (
                        <li key={`${p.apiId}:${p.viaApplicationId ?? "direct"}`}>
                          via{" "}
                          <Link to={`/catalog/apis/${p.apiId}`} className="text-primary hover:underline">
                            {p.apiName}
                          </Link>
                          {p.viaApplicationId ? (
                            <>
                              {" · "}
                              <Link
                                to={`/catalog/applications/${p.viaApplicationId}`}
                                className="text-primary hover:underline"
                              >
                                {p.viaApplicationDisplayName ?? "application"}
                              </Link>
                            </>
                          ) : null}
                        </li>
                      ))}
                    </ul>
                  </Table.Cell>
                </Table.Row>
              ))}
            </Table.Body>
          </Table>
        </div>
      )}
    </div>
  );
}

function DerivedDependenciesSkeleton() {
  return (
    <section className="space-y-6" aria-label="Derived dependencies">
      {["Dependencies", "Dependents"].map((title) => (
        <div className="space-y-2" key={title}>
          <h3 className="text-sm font-semibold text-primary">{title}</h3>
          <Table aria-label={title}>
            <DerivedTableHeader />
            <TableSkeleton rows={2} cells={3} />
          </Table>
        </div>
      ))}
    </section>
  );
}
