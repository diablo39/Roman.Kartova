import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useAuth } from "react-oidc-context";
import { apiClient, API_BASE_URL } from "./client";
import { useCursorList } from "@/lib/list/useCursorList";
import { throwWithStatus, unwrapData } from "@/shared/api/openapi-fetch-helpers";
import type { RegisterApiInput } from "../schemas/registerApi";
import type { components, operations } from "@/generated/openapi";

type ApiResponse = components["schemas"]["ApiResponse"];
type ListApisQuery = NonNullable<operations["ListApis"]["parameters"]["query"]>;

type ApisListParams = {
  sortBy: NonNullable<ListApisQuery["sortBy"]>;      // "displayName" | "style" | "version" | "createdAt"
  sortOrder: NonNullable<ListApisQuery["sortOrder"]>;
  limit?: number;
  /** ADR-0107 style multi-select (wire values rest|grpc|graphQL). Empty/undefined ⇒ omitted ⇒ show all. */
  style?: string[];
  /** ADR-0107 team multi-select (team ids). Empty/undefined ⇒ omitted ⇒ show all. */
  teamId?: string[];
  displayNameContains?: string;
};

export const apiKeys = {
  all: ["apis"] as const,
  list: (params?: ApisListParams) =>
    params ? ([...apiKeys.all, "list", params] as const) : ([...apiKeys.all, "list"] as const),
  detail: (id: string) => [...apiKeys.all, "detail", id] as const,
  spec: (id: string) => [...apiKeys.all, "spec", id] as const,
};

export function useApisList(params: ApisListParams) {
  return useCursorList<ApiResponse>({
    queryKey: apiKeys.list(params),
    fetchPage: async (cursor) => {
      const { data, error } = await apiClient.GET("/api/v1/catalog/apis", {
        params: {
          query: {
            sortBy: params.sortBy,
            sortOrder: params.sortOrder,
            limit: params.limit ?? 50,
            cursor,
            ...(params.style?.length ? { style: params.style } : {}),
            ...(params.teamId?.length ? { teamId: params.teamId } : {}),
            ...(params.displayNameContains ? { displayNameContains: params.displayNameContains } : {}),
          },
        },
      });
      if (error) throw error;
      return unwrapData(data);
    },
  });
}

export function useApi(id: string) {
  return useQuery({
    queryKey: apiKeys.detail(id),
    enabled: id !== "",
    queryFn: async () => {
      const { data, error } = await apiClient.GET("/api/v1/catalog/apis/{id}", { params: { path: { id } } });
      if (error) throw error;
      return unwrapData(data);
    },
  });
}

export function useRegisterApi() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (input: RegisterApiInput) => {
      const { data, error, response } = await apiClient.POST("/api/v1/catalog/apis", {
        body: { ...input, specUrl: input.specUrl || null },
      });
      if (error) throwWithStatus(error, response);
      return unwrapData(data);
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: apiKeys.all });
    },
  });
}

/**
 * GET /api/v1/catalog/apis/{id}/spec — fetches the raw stored spec document
 * (ADR-0112). Bypasses `apiClient` because openapi-fetch hard-codes
 * `application/json` for both bodies and response parsing, which would
 * mangle YAML specs; here we need the raw text plus the negotiated
 * `Content-Type` (media type) verbatim.
 *
 * 404 (no spec uploaded yet) resolves to `null` rather than an error state.
 */
export function useApiSpec(id: string, hasSpec: boolean) {
  const auth = useAuth();
  return useQuery({
    queryKey: apiKeys.spec(id),
    enabled: hasSpec && !!id,
    queryFn: async (): Promise<{ content: string; mediaType: string } | null> => {
      const token = auth.user?.access_token;
      const res = await fetch(`${API_BASE_URL}/api/v1/catalog/apis/${id}/spec`, {
        headers: token ? { Authorization: `Bearer ${token}` } : {},
      });
      if (res.status === 404) return null;
      if (!res.ok) throw new Error(`Failed to load spec: ${res.status}`);
      const content = await res.text();
      const mediaType = res.headers.get("Content-Type")?.split(";")[0]?.trim() ?? "application/json";
      return { content, mediaType };
    },
  });
}

/**
 * PUT /api/v1/catalog/apis/{id}/spec — uploads/replaces the raw stored spec
 * document. Bypasses `apiClient` for the same reason as `useApiSpec`: the
 * body must go over the wire byte-for-byte with a caller-controlled
 * `Content-Type`, not JSON-serialized.
 */
export function useUpsertApiSpec(id: string) {
  const auth = useAuth();
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ content, mediaType }: { content: string; mediaType: string }) => {
      const token = auth.user?.access_token;
      const res = await fetch(`${API_BASE_URL}/api/v1/catalog/apis/${id}/spec`, {
        method: "PUT",
        headers: {
          "Content-Type": mediaType,
          ...(token ? { Authorization: `Bearer ${token}` } : {}),
        },
        body: content,
      });
      if (!res.ok) {
        let body: Record<string, unknown> = {};
        try {
          body = (await res.json()) as Record<string, unknown>;
        } catch {
          // Non-JSON body (truncated proxy / empty error) — fall through
          // with just the synthesized fallback message.
        }
        throwWithStatus(
          { ...body, message: typeof body.detail === "string" ? body.detail : `Upload failed: ${res.status}` },
          res,
        );
      }
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: apiKeys.detail(id) });
      qc.invalidateQueries({ queryKey: apiKeys.spec(id) });
      qc.invalidateQueries({ queryKey: apiKeys.all });
    },
  });
}

export type { ApiResponse };
