// web/src/features/catalog/api/impact.ts
import { useQuery } from "@tanstack/react-query";
import { apiClient } from "./client";
import { unwrapData } from "@/shared/api/openapi-fetch-helpers";
import type { GraphResponse } from "@/features/catalog/api/graph";
import type { RelationshipKind } from "@/features/catalog/relationships/relationshipTypeRules";

export type ImpactSubject = { kind: RelationshipKind; id: string };

async function fetchImpact(s: ImpactSubject): Promise<GraphResponse> {
  const { data, error } = await apiClient.GET("/api/v1/catalog/impact", {
    params: { query: { entityKind: s.kind, entityId: s.id } },
  });
  if (error) throw error;
  return unwrapData(data);
}

export function useImpactAnalysis(subject: ImpactSubject | null) {
  return useQuery({
    queryKey: ["catalog", "impact", subject?.kind, subject?.id],
    queryFn: () => fetchImpact(subject!),
    enabled: subject != null,
  });
}
