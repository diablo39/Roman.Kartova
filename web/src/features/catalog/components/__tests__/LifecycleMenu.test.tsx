import type { ReactNode } from "react";
import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { Toaster } from "sonner";

import { LifecycleMenu } from "../LifecycleMenu";
import type { ApplicationResponse } from "@/features/catalog/api/applications";

// Wrapper supplies the QueryClient that the dialog mutations need (LifecycleMenu
// always mounts both confirm dialogs as siblings, even when closed).
function wrapper({ children }: { children: ReactNode }) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return (
    <QueryClientProvider client={qc}>
      <Toaster />
      {children}
    </QueryClientProvider>
  );
}

const baseApp: ApplicationResponse = {
  id: "00000000-0000-0000-0000-000000000abc",
  tenantId: "t",
  name: "n",
  displayName: "App",
  description: "d",
  ownerUserId: "u",
  createdAt: "2026-04-30T00:00:00Z",
  lifecycle: "active",
  sunsetDate: null,
  version: "v1",
};

const MS_PER_DAY = 24 * 60 * 60 * 1000;
// Build sunset dates relative to the real `Date.now()` so the
// before/after-sunset branches are deterministic without fake timers
// (react-aria's pointer interactions deadlock under `vi.useFakeTimers`).
const FAR_FUTURE = new Date(Date.now() + 365 * MS_PER_DAY).toISOString();
const PAST = new Date(Date.now() - 365 * MS_PER_DAY).toISOString();

describe("LifecycleMenu", () => {
  it("Active state shows Deprecate menu item", async () => {
    const user = userEvent.setup();
    render(
      <LifecycleMenu application={{ ...baseApp, lifecycle: "active", sunsetDate: null }} />,
      { wrapper }
    );
    await user.click(screen.getByRole("button", { name: /open lifecycle menu/i }));
    const item = await screen.findByRole("menuitem", { name: /deprecate/i });
    expect(item).toBeVisible();
  });

  it("Deprecated + before sunset disables Decommission", async () => {
    const user = userEvent.setup();
    render(
      <LifecycleMenu
        application={{ ...baseApp, lifecycle: "deprecated", sunsetDate: FAR_FUTURE }}
      />,
      { wrapper }
    );
    await user.click(screen.getByRole("button", { name: /open lifecycle menu/i }));
    const item = await screen.findByRole("menuitem", { name: /decommission/i });
    expect(item).toHaveAttribute("aria-disabled", "true");
  });

  it("Deprecated + after sunset enables Decommission", async () => {
    const user = userEvent.setup();
    render(
      <LifecycleMenu
        application={{ ...baseApp, lifecycle: "deprecated", sunsetDate: PAST }}
      />,
      { wrapper }
    );
    await user.click(screen.getByRole("button", { name: /open lifecycle menu/i }));
    const item = await screen.findByRole("menuitem", { name: /decommission/i });
    expect(item).not.toHaveAttribute("aria-disabled", "true");
  });

  it("Decommissioned does not render a dropdown trigger (badge only)", () => {
    render(
      <LifecycleMenu
        application={{ ...baseApp, lifecycle: "decommissioned", sunsetDate: PAST }}
      />,
      { wrapper }
    );
    expect(screen.queryByRole("button", { name: /open lifecycle menu/i })).toBeNull();
    // Badge text still rendered.
    expect(screen.getByText(/decommissioned/i)).toBeInTheDocument();
  });
});
