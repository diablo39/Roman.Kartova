import { Navigate, Route, Routes } from "react-router-dom";
import { RequireAuth } from "@/shared/oidc/RequireAuth";
import { AdminLayout } from "./layout/AdminLayout";
import { AdminLandingPage } from "./pages/AdminLandingPage";
import { AdminCallbackPage } from "./pages/AdminCallbackPage";

function ProtectedAdminShell() {
  return (
    <RequireAuth>
      <AdminLayout />
    </RequireAuth>
  );
}

/** "No access" is a state of AdminLayout, not a route — it can be neither deep-linked nor bypassed. */
export function AdminRoutes() {
  return (
    <Routes>
      <Route path="/callback" element={<AdminCallbackPage />} />
      <Route element={<ProtectedAdminShell />}>
        <Route index element={<AdminLandingPage />} />
      </Route>
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  );
}
