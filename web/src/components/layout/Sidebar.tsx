import { usePermissions } from "@/shared/auth/usePermissions";
import { KartovaPermissions } from "@/shared/auth/permissions";
import { SidebarFrame } from "./SidebarFrame";
import { NavGroup, NavItemLink, NavCollapsibleGroup, DisabledItem } from "./sidebar-nav";

export function Sidebar() {
  const { hasPermission } = usePermissions();
  const canSeeTeams = hasPermission(KartovaPermissions.TeamRead);
  const canSeeMembers = hasPermission(KartovaPermissions.OrgUsersRead);
  const canSeeOrgSettings = hasPermission(KartovaPermissions.OrgProfileRead);
  const canSeeInvitations = hasPermission(KartovaPermissions.OrgInvitationsRead);

  return (
    <SidebarFrame>
      <NavGroup title="Catalog" className="mt-0">
        <div className="space-y-1">
          <NavCollapsibleGroup title="Software" storageKey="software">
            <li>
              <NavItemLink to="/catalog/applications" label="Applications" />
            </li>
            <li>
              <NavItemLink to="/catalog/services" label="Services" />
            </li>
            <li>
              <NavItemLink to="/catalog/apis" label="APIs" />
            </li>
            <li>
              <NavItemLink to="/catalog/systems" label="Systems" />
            </li>
          </NavCollapsibleGroup>
          <NavCollapsibleGroup title="Infrastructure" storageKey="infrastructure">
            <li>
              <NavItemLink to="/catalog/infrastructure" label="All Objects" end />
            </li>
            <li>
              <NavItemLink to="/catalog/infrastructure/vms" label="Virtual Machines" />
            </li>
            <li>
              <NavItemLink to="/catalog/environments" label="Environments" />
            </li>
            <li>
              <DisabledItem label="Brokers" />
            </li>
          </NavCollapsibleGroup>
          <ul className="space-y-1">
            <li>
              <NavItemLink to="/catalog/hierarchy" label="Hierarchy" />
            </li>
            <li>
              <DisabledItem label="Docs" />
            </li>
          </ul>
        </div>
      </NavGroup>
      {(canSeeTeams || canSeeMembers) && (
        <ul className="mt-4 space-y-1">
          {canSeeTeams && (
            <li>
              <NavItemLink to="/teams" label="Teams" />
            </li>
          )}
          {canSeeMembers && (
            <li>
              <NavItemLink to="/members" label="Members" />
            </li>
          )}
        </ul>
      )}
      {canSeeOrgSettings && (
        <NavGroup title="Settings">
          <ul className="space-y-1">
            <li>
              <NavItemLink to="/settings/organization" label="Organization" />
            </li>
            {canSeeInvitations && (
              <li>
                <NavItemLink to="/settings/invitations" label="Invitations" />
              </li>
            )}
          </ul>
        </NavGroup>
      )}
    </SidebarFrame>
  );
}
