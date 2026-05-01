import { Link } from "react-router-dom";
import { Table } from "@/components/application/table/table";
import { Skeleton } from "@/components/base/skeleton/skeleton";
import { Card, CardContent } from "@/components/base/card/card";

export interface ApplicationRow {
  id: string;
  name: string;
  displayName: string;
  description: string;
  ownerUserId?: string;
  createdAt?: string;
}

interface ApplicationsTableProps {
  isLoading: boolean;
  applications: ApplicationRow[] | undefined;
}

const SKELETON_ROW_COUNT = 5;

export function ApplicationsTable({ isLoading, applications }: ApplicationsTableProps) {
  if (isLoading) {
    return (
      <Table aria-label="Applications">
        <Table.Header>
          <Table.Head id="name">Name</Table.Head>
          <Table.Head id="description">Description</Table.Head>
        </Table.Header>
        <Table.Body>
          {Array.from({ length: SKELETON_ROW_COUNT }).map((_, i) => (
            <Table.Row key={i} id={`skeleton-${i}`} data-testid="row-skeleton">
              <Table.Cell><Skeleton className="h-5 w-40" /></Table.Cell>
              <Table.Cell><Skeleton className="h-5 w-72" /></Table.Cell>
            </Table.Row>
          ))}
        </Table.Body>
      </Table>
    );
  }

  if (!applications || applications.length === 0) {
    return (
      <Card className="mx-auto max-w-md text-center">
        <CardContent className="space-y-2 p-8">
          <p className="text-base font-medium text-primary">No applications yet</p>
          <p className="text-sm text-tertiary">
            Use the &quot;+ Register Application&quot; button in the header to add your first one.
          </p>
        </CardContent>
      </Card>
    );
  }

  return (
    <Table aria-label="Applications">
      <Table.Header>
        <Table.Head id="name">Name</Table.Head>
        <Table.Head id="description">Description</Table.Head>
      </Table.Header>
      <Table.Body>
        {applications.map(app => (
          <Table.Row key={app.id} id={app.id}>
            <Table.Cell>
              <Link
                to={`/catalog/applications/${app.id}`}
                className="block font-medium text-primary hover:underline"
              >
                {app.displayName}
              </Link>
              <span className="font-mono text-xs text-tertiary">{app.name}</span>
            </Table.Cell>
            <Table.Cell className="text-sm text-tertiary">
              {app.description || <span className="italic">No description</span>}
            </Table.Cell>
          </Table.Row>
        ))}
      </Table.Body>
    </Table>
  );
}
