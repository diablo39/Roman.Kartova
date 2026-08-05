import { Link } from "react-router-dom";
import { entityDetailPath } from "@/features/catalog/relationships/graphModel";

interface Props {
  /** Present only on list/detail reads — write-path responses leave both fields null. */
  systemId?: string | null;
  systemDisplayName?: string | null;
}

/**
 * The System a component belongs to (ADR-0111 amended: at most one). Both fields are needed
 * to render a link — a half-populated pair degrades to "unassigned" rather than an empty label.
 */
export function SystemCell({ systemId, systemDisplayName }: Props) {
  if (!systemId || !systemDisplayName) {
    return <span className="text-tertiary">—</span>;
  }
  return (
    <Link to={entityDetailPath("system", systemId)} className="text-primary hover:underline">
      {systemDisplayName}
    </Link>
  );
}
