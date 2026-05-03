import { SearchSm, ChevronDown, LogOut01 } from "@untitledui/icons";
import { Button as AriaButton } from "react-aria-components";
import { useAuth } from "react-oidc-context";
import { useCurrentOrganization } from "@/features/organization/api/me";
import { useCurrentUser } from "@/shared/auth/useCurrentUser";
import { initialsOf } from "@/shared/auth/initials";
import { Avatar } from "@/components/base/avatar/avatar";
import { Badge } from "@/components/base/badges/badges";
import { Skeleton } from "@/components/base/skeleton/skeleton";
import { Dropdown } from "@/components/base/dropdown/dropdown";

export function TopBar() {
  const orgQuery = useCurrentOrganization();
  const user = useCurrentUser();
  const auth = useAuth();

  const initials = initialsOf(user?.displayName);

  return (
    <header className="flex h-14 items-center gap-4 border-b border-secondary bg-primary px-6">
      {/* Tenant pill */}
      <div data-testid="tenant-pill" className="flex items-center">
        {orgQuery.isLoading ? (
          <Skeleton className="h-6 w-32" data-testid="tenant-skeleton" />
        ) : orgQuery.isSuccess ? (
          <Badge color="gray" type="pill-color" size="sm" className="uppercase tracking-wide">
            {orgQuery.data.name}
          </Badge>
        ) : null}
      </div>

      {/* Search (disabled placeholder) */}
      <div className="relative ml-auto w-full max-w-xl">
        <SearchSm className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-fg-quaternary" />
        <input
          type="text"
          placeholder="Search entities..."
          disabled
          className="w-full rounded-md border border-secondary bg-primary py-2 pl-9 pr-3 text-sm text-secondary placeholder:text-tertiary disabled:cursor-not-allowed"
        />
      </div>

      {/* User avatar + dropdown */}
      <Dropdown.Root>
        <AriaButton
          data-testid="user-menu"
          aria-label="Open user menu"
          className="flex cursor-pointer items-center gap-1.5 rounded-full p-0.5 outline-none hover:bg-primary_hover focus-visible:ring-2 focus-visible:ring-brand-500"
        >
          <Avatar size="sm" initials={initials} />
          <ChevronDown className="h-4 w-4 text-fg-quaternary" />
        </AriaButton>
        <Dropdown.Popover className="w-56" placement="bottom right">
          {user && (
            <div className="px-3 py-2 text-sm">
              <div className="font-medium text-primary">{user.displayName}</div>
              <div className="text-xs text-tertiary">{user.email}</div>
            </div>
          )}
          <Dropdown.Menu>
            {user && <Dropdown.Separator />}
            <Dropdown.Item
              label="Sign out"
              icon={LogOut01}
              onAction={() => void auth.signoutRedirect()}
            />
          </Dropdown.Menu>
        </Dropdown.Popover>
      </Dropdown.Root>
    </header>
  );
}
