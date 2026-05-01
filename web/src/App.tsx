import { Providers } from "./app/providers";
import { AppRoutes } from "./app/router";

export default function App() {
  return (
    <Providers>
      <AppRoutes />
    </Providers>
  );
}
