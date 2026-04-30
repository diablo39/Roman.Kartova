import { Navigate, Route, Routes } from "react-router-dom";
import { RequireAuth } from "@/shared/auth/RequireAuth";
import { AppLayout } from "@/components/layout/AppLayout";
import { CatalogListPage } from "@/features/catalog/pages/CatalogListPage";
import { ApplicationDetailPage } from "@/features/catalog/pages/ApplicationDetailPage";

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
      <Route
        path="/callback"
        element={<div className="p-8 text-sm">Completing sign-in…</div>}
      />
      <Route element={<ProtectedShell />}>
        <Route path="/catalog" element={<CatalogListPage />} />
        <Route
          path="/catalog/applications/:id"
          element={<ApplicationDetailPage />}
        />
      </Route>
      <Route path="*" element={<div className="p-8 text-sm">Not found</div>} />
    </Routes>
  );
}
