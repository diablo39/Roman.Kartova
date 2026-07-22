import { lazy, Suspense } from "react";
import { Navigate, Route, Routes } from "react-router-dom";
import { RequireAuth } from "@/shared/auth/RequireAuth";
import { AppLayout } from "@/components/layout/AppLayout";
import { CatalogListPage } from "@/features/catalog/pages/CatalogListPage";
import { ApplicationDetailPage } from "@/features/catalog/pages/ApplicationDetailPage";
import { ServicesListPage } from "@/features/catalog/pages/ServicesListPage";
import { ServiceDetailPage } from "@/features/catalog/pages/ServiceDetailPage";
import { ApisListPage } from "@/features/catalog/pages/ApisListPage";
import { ApiDetailPage } from "@/features/catalog/pages/ApiDetailPage";
import { SystemsListPage } from "@/features/catalog/pages/SystemsListPage";
import { SystemDetailPage } from "@/features/catalog/pages/SystemDetailPage";
import { TeamsListPage } from "@/features/teams/pages/TeamsListPage";
import { TeamDetailPage } from "@/features/teams/pages/TeamDetailPage";
import { WelcomePage } from "@/features/auth/pages/WelcomePage";
import { LoginErrorPage } from "@/features/auth/pages/LoginErrorPage";
import { OrganizationSettingsPage } from "@/features/organization/pages/OrganizationSettingsPage";
import { InvitationsPage } from "@/features/organization/pages/InvitationsPage";
import { UserDetailPage } from "@/features/users/pages/UserDetailPage";
import { CallbackPage } from "./CallbackPage";
import { AcceptInvitationPage } from "@/features/invitations/pages/AcceptInvitationPage";
import { MembersListPage } from "@/features/members/pages/MembersListPage";

const GraphExplorerPage = lazy(() =>
  import("@/features/catalog/pages/GraphExplorerPage").then((m) => ({ default: m.GraphExplorerPage })),
);

function ProtectedShell() {
  return (
    <RequireAuth>
      <AppLayout />
    </RequireAuth>
  );
}

export function AppRoutes() {
  return (
    <Routes>
      <Route path="/" element={<Navigate to="/catalog" replace />} />
      <Route path="/callback" element={<CallbackPage />} />
      {/* /welcome and /login-error are intentionally outside <ProtectedShell> —
          both are part of the auth handshake itself (slice-9 F6). /welcome
          renders post-callback before the user has landed; /login-error is
          shown when the auth handshake itself failed. */}
      <Route path="/welcome" element={<WelcomePage />} />
      <Route path="/login-error" element={<LoginErrorPage />} />
      <Route path="/accept-invitation" element={<AcceptInvitationPage />} />
      <Route element={<ProtectedShell />}>
        {/* /catalog is a redirect alias to its first sub-page so "home"
            fallbacks (root redirect, OIDC callback, /welcome, returnTo default)
            can keep targeting /catalog without hard-coding the sub-page. */}
        <Route path="/catalog" element={<Navigate to="/catalog/applications" replace />} />
        <Route path="/catalog/applications" element={<CatalogListPage />} />
        <Route
          path="/catalog/applications/:id"
          element={<ApplicationDetailPage />}
        />
        <Route path="/catalog/services" element={<ServicesListPage />} />
        <Route path="/catalog/services/:id" element={<ServiceDetailPage />} />
        <Route path="/catalog/apis" element={<ApisListPage />} />
        <Route path="/catalog/apis/:id" element={<ApiDetailPage />} />
        <Route path="/catalog/systems" element={<SystemsListPage />} />
        <Route path="/catalog/systems/:id" element={<SystemDetailPage />} />
        <Route path="/teams" element={<TeamsListPage />} />
        <Route path="/teams/:id" element={<TeamDetailPage />} />
        <Route path="/settings/organization" element={<OrganizationSettingsPage />} />
        <Route path="/settings/invitations" element={<InvitationsPage />} />
        <Route path="/users/:id" element={<UserDetailPage />} />
        <Route path="/members" element={<MembersListPage />} />
        <Route
          path="/graph"
          element={
            <Suspense fallback={<div className="p-8 text-sm text-tertiary">Loading graph…</div>}>
              <GraphExplorerPage />
            </Suspense>
          }
        />
      </Route>
      <Route path="*" element={<div className="p-8 text-sm">Not found</div>} />
    </Routes>
  );
}
