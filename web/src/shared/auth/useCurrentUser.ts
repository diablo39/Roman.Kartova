import { useAuth } from "react-oidc-context";

export interface CurrentUser {
  userId: string;
  displayName: string;
  email: string;
  tenantId: string;
  accessToken: string;
}

export function useCurrentUser(): CurrentUser | null {
  const auth = useAuth();
  if (!auth.isAuthenticated || !auth.user) return null;
  const p = auth.user.profile as Record<string, unknown>;
  return {
    userId: String(p.sub ?? ""),
    displayName: String(p.name ?? p.preferred_username ?? p.email ?? ""),
    email: String(p.email ?? ""),
    tenantId: String(p.tenant_id ?? ""),
    accessToken: auth.user.access_token,
  };
}
