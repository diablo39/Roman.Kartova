import { Link } from "react-router-dom";
import { Badge } from "@/components/base/badges/badges";
import { Table } from "@/components/application/table/table";
import { TableSkeleton, TablePager } from "@/components/application/data-table/data-table";
import { useRelationshipsList } from "@/features/catalog/api/relationships";
import { entityDetailPath, ENTITY_KIND_LABEL } from "@/features/catalog/relationships/graphModel";
import { isRelationshipKind } from "@/features/catalog/relationships/relationshipTypeRules";

interface Props {
  systemId: string;
}

// Members are the components PARTOF this System — the SOURCE side of every incoming
// edge. Write-time rules (RelationshipTypeRules.IsAllowedPair) restrict incoming-to-System
// edges to PartOf, but the READ path (ListRelationshipsForEntityHandler) applies no type
// filter — so we filter to `partOf` client-side to stay drift-tolerant against backfills /
// direct DB writes (ADR-0111 amendment hedge; cf. e2e/tests/relationship-drift.spec.ts).
export function SystemMembersSection({ systemId }: Props) {
  const members = useRelationshipsList({ entityKind: "system", entityId: systemId, direction: "incoming" });
  const rows = members.items.filter((r) => r.type === "partOf");

  return (
    <section className="space-y-2" aria-label="Members">
      <h3 className="text-sm font-semibold text-primary">Members</h3>
      {members.isLoading ? (
        <Table aria-label="Members">
          <Table.Header>
            <Table.Head id="entity" isRowHeader>
              Component
            </Table.Head>
            <Table.Head id="kind">Kind</Table.Head>
          </Table.Header>
          <TableSkeleton rows={3} cells={2} />
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
            </Table.Header>
            <Table.Body>
              {rows.map((r) => {
                const m = r.source;
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
    </section>
  );
}
