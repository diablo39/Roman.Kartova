import { AdminProviders } from "./providers";
import { AdminRoutes } from "./router";

export function AdminApp() {
  return (
    <AdminProviders>
      <AdminRoutes />
    </AdminProviders>
  );
}
