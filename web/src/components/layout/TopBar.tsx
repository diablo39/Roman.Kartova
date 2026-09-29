import { SearchSm } from "@untitledui/icons";
import { useAuth } from "react-oidc-context";
import { useOrgProfile, useLogoUrl } from "@/features/organization/api/organization";
import { useCurrentUser } from "@/shared/auth/useCurrentUser";
import { Badge } from "@/components/base/badges/badges";
import { Skeleton } from "@/components/base/skeleton/skeleton";
import { TopBarFrame } from "./TopBarFrame";

export function TopBar() {
  const orgQuery = useOrgProfile();
  const logoUrl = useLogoUrl();
  const user = useCurrentUser();
  const auth = useAuth();

  return (
    <TopBarFrame
      identity={
        // Tenant identity — renders the uploaded logo when available (Slice-9 F2/F7),
        // else falls back to the org displayName as a gray pill.
        <div data-testid="tenant-pill" className="flex items-center gap-2">
          {orgQuery.isLoading ? (
            <Skeleton className="h-8 w-32" data-testid="tenant-skeleton" />
          ) : orgQuery.isSuccess ? (
            logoUrl ? (
              <img
                src={logoUrl}
                alt={orgQuery.data.displayName}
                className="h-8 w-8 rounded object-contain"
                data-testid="tenant-logo"
              />
            ) : (
              <Badge color="gray" type="pill-color" size="sm" className="uppercase tracking-wide">
                {orgQuery.data.displayName}
              </Badge>
            )
          ) : null}
        </div>
      }
      center={
        // Search (disabled placeholder)
        <div className="relative ml-auto w-full max-w-xl">
          <SearchSm className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-fg-quaternary" />
          <input
            type="text"
            placeholder="Search entities..."
            disabled
            className="w-full rounded-md border border-secondary bg-primary py-2 pl-9 pr-3 text-sm text-secondary placeholder:text-tertiary disabled:cursor-not-allowed"
          />
        </div>
      }
      user={user ? { displayName: user.displayName, email: user.email } : null}
      onSignOut={() => void auth.signoutRedirect()}
    />
  );
}
