import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useAuth } from "react-oidc-context";
import { apiClient } from "@/features/catalog/api/client";
import {
  throwWithStatus,
  unwrapData,
} from "@/shared/api/openapi-fetch-helpers";
import type { components } from "@/generated/openapi";

type OrgProfileResponse = components["schemas"]["OrgProfileResponse"];
type UpdateOrgProfileRequest = components["schemas"]["UpdateOrgProfileRequest"];
type UploadLogoResponse = components["schemas"]["UploadLogoResponse"];

export const orgKeys = {
  profile: () => ["org", "profile"] as const,
  logoUrl: (etag: string | null) => ["org", "logo", etag ?? ""] as const,
};

/**
 * GET /api/v1/organizations/me — fetches the current tenant's organization
 * profile (slice-9 spec §4). The response includes `logoEtag` / `logoMimeType`
 * for the logo metadata so the SPA can compose a cache-friendly logo URL via
 * `useLogoUrl()` without a second round trip.
 */
export function useOrgProfile() {
  return useQuery<OrgProfileResponse>({
    queryKey: orgKeys.profile(),
    queryFn: async ({ signal }) => {
      const { data, error } = await apiClient.GET(
        "/api/v1/organizations/me",
        { signal },
      );
      if (error) throw error;
      return unwrapData(data);
    },
    staleTime: 60_000,
  });
}

/**
 * PUT /api/v1/organizations/me — updates the current tenant's profile
 * (slice-9 spec §4). The `If-Match` header carries the optimistic concurrency
 * token (ADR-0096); the backend reserves the contract today and will start
 * enforcing it once the EF concurrency token lands, so callers should pass
 * `ifMatch` whenever they have a fresh version from a prior `GET /me`.
 *
 * On 412 the hook re-throws with `__status: 412` attached so a future profile
 * dialog can branch on the conflict without re-parsing the response body.
 */
export function useUpdateOrgProfile() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (input: UpdateOrgProfileRequest & { ifMatch?: string }) => {
      const { ifMatch, ...body } = input;
      const { error, response } = await apiClient.PUT(
        "/api/v1/organizations/me",
        {
          body,
          headers: ifMatch ? { "If-Match": ifMatch } : undefined,
        },
      );
      if (error) throwWithStatus(error, response);
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: orgKeys.profile() }),
  });
}

/**
 * Composes the URL the `<img>` tag should point at to render the current
 * organization's logo. The `?v=<etag>` query param is the cache-bust signal:
 * a new upload changes `logoEtag` on the profile response, which changes the
 * URL, which forces the browser to revalidate.
 *
 * Returns `null` when no logo has been uploaded yet (server returns 404 on
 * `GET /me/logo`) so callers can render a placeholder without an extra check.
 */
export function useLogoUrl(): string | null {
  const { data } = useOrgProfile();
  if (!data?.logoEtag) return null;
  return `/api/v1/organizations/me/logo?v=${encodeURIComponent(data.logoEtag)}`;
}

/**
 * Base URL the SPA targets for every API call. The default
 * (`http://localhost:8080`) lines up with `docker compose up`'s API origin
 * and matches the apiClient configuration in `features/catalog/api/client.ts`.
 * In production both origins collapse to the same host so `VITE_API_BASE_URL`
 * (typically unset) is the empty string — relative paths Just Work.
 *
 * Exported for test fixtures that need to assert the URL composition.
 */
export const API_BASE_URL =
  import.meta.env.VITE_API_BASE_URL ?? "http://localhost:8080";

/**
 * PUT /api/v1/organizations/me/logo — uploads new logo bytes. The endpoint
 * accepts a raw binary body (`image/png`, `image/jpeg`, `image/svg+xml`) and
 * returns the new strong ETag + the negotiated MIME type. We bypass
 * `apiClient` because openapi-fetch hard-codes `application/json` for typed
 * bodies and rebuilds the payload as JSON; here we need the raw Blob byte-for-byte
 * with a caller-controlled `Content-Type`.
 *
 * The URL is prefixed with `API_BASE_URL` so the request hits the API origin
 * (`http://localhost:8080` in dev) rather than the SPA dev-server origin
 * (`http://localhost:5173`). The H4 Playwright verification surfaced the
 * silent 404 that the previous relative-URL implementation produced when no
 * Vite proxy was configured — see `docs/superpowers/plans/slice-9-docker-verification.md`
 * bug SPA-1.
 */
export function useUploadOrgLogo() {
  const auth = useAuth();
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({
      bytes,
      mimeType,
    }: {
      bytes: Blob;
      mimeType: string;
    }): Promise<UploadLogoResponse> => {
      const token = auth.user?.access_token;
      if (!token) throw new Error("Not authenticated");
      const response = await fetch(
        `${API_BASE_URL}/api/v1/organizations/me/logo`,
        {
          method: "PUT",
          headers: {
            "Content-Type": mimeType,
            Authorization: `Bearer ${token}`,
          },
          body: bytes,
        },
      );
      if (!response.ok) {
        const error: Record<string, unknown> = {
          message: `Upload failed: ${response.status}`,
        };
        throwWithStatus(error, response);
      }
      return (await response.json()) as UploadLogoResponse;
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: orgKeys.profile() }),
  });
}

/**
 * DELETE /api/v1/organizations/me/logo — removes the stored logo. 204 on
 * success; 404 if no logo exists (treated as success by the dialog — the
 * end state is the same).
 */
export function useDeleteOrgLogo() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async () => {
      const { error, response } = await apiClient.DELETE(
        "/api/v1/organizations/me/logo",
        {},
      );
      if (error) throwWithStatus(error, response);
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: orgKeys.profile() }),
  });
}

export type { OrgProfileResponse, UpdateOrgProfileRequest, UploadLogoResponse };
