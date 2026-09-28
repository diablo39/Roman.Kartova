import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { BrowserRouter } from "react-router-dom";
// Same self-hosted Inter weights as the tenant entry (web/src/main.tsx) — identical master-shell typography.
import "@fontsource/inter/400.css";
import "@fontsource/inter/500.css";
import "@fontsource/inter/600.css";
import "@fontsource/inter/700.css";
import "../index.css";
import { AdminApp } from "./App";

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <BrowserRouter>
      <AdminApp />
    </BrowserRouter>
  </StrictMode>,
);
