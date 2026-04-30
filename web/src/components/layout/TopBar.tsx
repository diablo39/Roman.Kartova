import { Search, ChevronDown, LogOut } from "lucide-react";
import { useAuth } from "react-oidc-context";
import { useCurrentOrganization } from "@/features/organization/api/me";
import { useCurrentUser } from "@/shared/auth/useCurrentUser";
import { Avatar, AvatarFallback } from "@/components/ui/avatar";
import { Badge } from "@/components/ui/badge";
import { Skeleton } from "@/components/ui/skeleton";
import { Button } from "@/components/ui/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";

export function TopBar() {
  const orgQuery = useCurrentOrganization();
  const user = useCurrentUser();
  const auth = useAuth();

  const initials =
    user?.displayName
      ?.split(/\s+/)
      .filter(Boolean)
      .slice(0, 2)
      .map(part => part[0]?.toUpperCase())
      .join("") ?? "?";

  return (
    <header className="flex h-14 items-center gap-4 border-b border-border bg-card px-6">
      {/* Tenant pill */}
      <div data-testid="tenant-pill" className="flex items-center">
        {orgQuery.isLoading ? (
          <Skeleton className="h-6 w-32" data-testid="tenant-skeleton" />
        ) : orgQuery.isSuccess ? (
          <Badge variant="secondary" className="text-xs uppercase tracking-wide">
            {orgQuery.data.name}
          </Badge>
        ) : null}
      </div>

      {/* Search (disabled placeholder) */}
      <div className="relative ml-auto w-full max-w-xl">
        <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
        <input
          type="text"
          placeholder="Search entities..."
          disabled
          className="w-full rounded-md border border-border bg-background py-2 pl-9 pr-3 text-sm text-muted-foreground placeholder:text-muted-foreground disabled:cursor-not-allowed"
        />
      </div>

      {/* User avatar + dropdown */}
      <DropdownMenu>
        <DropdownMenuTrigger asChild>
          <Button variant="ghost" className="flex items-center gap-2 px-2" data-testid="user-menu">
            <Avatar className="h-8 w-8">
              <AvatarFallback className="text-xs">{initials}</AvatarFallback>
            </Avatar>
            <ChevronDown className="h-4 w-4 text-muted-foreground" />
          </Button>
        </DropdownMenuTrigger>
        <DropdownMenuContent align="end" className="w-56">
          {user && (
            <>
              <div className="px-2 py-1.5 text-sm">
                <div className="font-medium">{user.displayName}</div>
                <div className="text-xs text-muted-foreground">{user.email}</div>
              </div>
              <DropdownMenuSeparator />
            </>
          )}
          <DropdownMenuItem onClick={() => void auth.signoutRedirect()}>
            <LogOut className="mr-2 h-4 w-4" /> Sign out
          </DropdownMenuItem>
        </DropdownMenuContent>
      </DropdownMenu>
    </header>
  );
}
