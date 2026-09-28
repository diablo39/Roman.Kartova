import { ChevronDown, LogOut01 } from "@untitledui/icons";
import { Button as AriaButton } from "react-aria-components";
import { Avatar } from "@/components/base/avatar/avatar";
import { Dropdown } from "@/components/base/dropdown/dropdown";
import { orDash } from "@/lib/utils/format";
import { initialsOf } from "@/lib/utils/initials";

export interface TopBarUser {
  displayName: string;
  email: string;
}

/**
 * Master-shell top bar chrome: identity slot (left), optional center slot (search), user menu (right).
 * Hook-free so both the tenant SPA and the admin console compose it with their own data (ADR-0118).
 */
export function TopBarFrame({
  identity,
  center,
  user,
  onSignOut,
}: {
  identity: React.ReactNode;
  center?: React.ReactNode;
  user: TopBarUser | null;
  onSignOut: () => void;
}) {
  const initials = initialsOf(user?.displayName);

  return (
    <header className="flex h-14 items-center gap-4 border-b border-secondary bg-primary px-6">
      {identity}
      {center ?? <div className="ml-auto" />}
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
              <div className="font-medium text-primary">{orDash(user.displayName)}</div>
              <div className="text-xs text-tertiary">{orDash(user.email)}</div>
            </div>
          )}
          <Dropdown.Menu>
            {user && <Dropdown.Separator />}
            <Dropdown.Item label="Sign out" icon={LogOut01} onAction={onSignOut} />
          </Dropdown.Menu>
        </Dropdown.Popover>
      </Dropdown.Root>
    </header>
  );
}
