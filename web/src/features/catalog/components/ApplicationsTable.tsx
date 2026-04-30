import { Link } from "react-router-dom";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { Skeleton } from "@/components/ui/skeleton";
import { Card, CardContent } from "@/components/ui/card";

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
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>Name</TableHead>
            <TableHead>Description</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {Array.from({ length: SKELETON_ROW_COUNT }).map((_, i) => (
            <TableRow key={i} data-testid="row-skeleton">
              <TableCell><Skeleton className="h-5 w-40" /></TableCell>
              <TableCell><Skeleton className="h-5 w-72" /></TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    );
  }

  if (!applications || applications.length === 0) {
    return (
      <Card className="mx-auto max-w-md text-center">
        <CardContent className="space-y-2 p-8">
          <p className="text-base font-medium">No applications yet</p>
          <p className="text-sm text-muted-foreground">
            Use the &quot;+ Register Application&quot; button in the header to add your first one.
          </p>
        </CardContent>
      </Card>
    );
  }

  return (
    <Table>
      <TableHeader>
        <TableRow>
          <TableHead>Name</TableHead>
          <TableHead>Description</TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        {applications.map(app => (
          <TableRow key={app.id}>
            <TableCell>
              <Link
                to={`/catalog/applications/${app.id}`}
                className="block font-medium hover:underline"
              >
                {app.displayName}
              </Link>
              <span className="font-mono text-xs text-muted-foreground">{app.name}</span>
            </TableCell>
            <TableCell className="text-sm text-muted-foreground">
              {app.description || <span className="italic">No description</span>}
            </TableCell>
          </TableRow>
        ))}
      </TableBody>
    </Table>
  );
}
