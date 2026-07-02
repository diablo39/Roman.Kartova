import type { ReactNode } from "react";
import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { Toaster } from "sonner";

import { LifecycleMenu } from "../LifecycleMenu";
import type { ApplicationResponse } from "@/features/catalog/api/applications";

// Wrapper supplies the QueryClient that the confirm dialogs' mutations need
// when the user opens one. Confirm dialogs are mounted on demand (gated by
// the menu's `openDialog` state) but the QueryClient must be present for
// hooks within them in any test that drives them.
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
  displayName: "App",
  description: "d",
  createdByUserId: "u",
  createdAt: "2026-04-30T00:00:00Z",
  lifecycle: "active",
  sunsetDate: null,
  teamId: "team-1",
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

  it("Deprecated + before sunset + canOverride enables Decommission (override reachable)", async () => {
    // ADR-0073 admin override: an override holder must be able to open the
    // decommission dialog before sunset to use the override checkbox. Without
    // canOverride the item is disabled (previous case); with it, enabled.
    const user = userEvent.setup();
    render(
      <LifecycleMenu
        application={{ ...baseApp, lifecycle: "deprecated", sunsetDate: FAR_FUTURE }}
        canOverride
      />,
      { wrapper }
    );
    await user.click(screen.getByRole("button", { name: /open lifecycle menu/i }));
    const item = await screen.findByRole("menuitem", { name: /decommission/i });
    expect(item).not.toHaveAttribute("aria-disabled", "true");
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

  it("Decommissioned + canReverse=false does not render a dropdown trigger (badge only)", () => {
    render(
      <LifecycleMenu
        application={{ ...baseApp, lifecycle: "decommissioned", sunsetDate: PAST }}
        canReverse={false}
      />,
      { wrapper }
    );
    expect(screen.queryByRole("button", { name: /open lifecycle menu/i })).toBeNull();
    // Badge text still rendered.
    expect(screen.getByText(/decommissioned/i)).toBeInTheDocument();
  });
});

// ---------------------------------------------------------------------------
// canForward prop — Viewer hides forward items (Slice 7)
// ---------------------------------------------------------------------------

describe("LifecycleMenu — canForward gating", () => {
  it("hides dropdown trigger for active app when canForward=false (canReverse defaults true but no reverse items for active)", () => {
    render(
      <LifecycleMenu
        application={{ ...baseApp, lifecycle: "active", sunsetDate: null }}
        canForward={false}
      />,
      { wrapper }
    );
    // Active lifecycle has no reverse items, so items list is empty — badge only, no trigger.
    expect(screen.queryByRole("button", { name: /open lifecycle menu/i })).toBeNull();
  });

  it("shows Deprecate item for active app when canForward=true (default)", async () => {
    const user = userEvent.setup();
    render(
      <LifecycleMenu
        application={{ ...baseApp, lifecycle: "active", sunsetDate: null }}
        canForward={true}
      />,
      { wrapper }
    );
    await user.click(screen.getByRole("button", { name: /open lifecycle menu/i }));
    expect(await screen.findByRole("menuitem", { name: /deprecate/i })).toBeVisible();
  });

  it("hides dropdown trigger for deprecated app when canForward=false and canReverse=false", () => {
    render(
      <LifecycleMenu
        application={{ ...baseApp, lifecycle: "deprecated", sunsetDate: PAST }}
        canForward={false}
        canReverse={false}
      />,
      { wrapper }
    );
    expect(screen.queryByRole("button", { name: /open lifecycle menu/i })).toBeNull();
  });
});

// ---------------------------------------------------------------------------
// canReverse prop — OrgAdmin reverse-lifecycle items (Slice 7)
// ---------------------------------------------------------------------------

describe("LifecycleMenu — canReverse gating", () => {
  it("canReverse=true + lifecycle=deprecated shows Reactivate item", async () => {
    const user = userEvent.setup();
    render(
      <LifecycleMenu
        application={{ ...baseApp, lifecycle: "deprecated", sunsetDate: FAR_FUTURE }}
        canReverse={true}
        canForward={false}
      />,
      { wrapper }
    );
    await user.click(screen.getByRole("button", { name: /open lifecycle menu/i }));
    expect(await screen.findByRole("menuitem", { name: /reactivate/i })).toBeVisible();
  });

  it("canReverse=true + lifecycle=decommissioned shows both Reactivate and Restore to Deprecated", async () => {
    const user = userEvent.setup();
    render(
      <LifecycleMenu
        application={{ ...baseApp, lifecycle: "decommissioned", sunsetDate: PAST }}
        canReverse={true}
        canForward={false}
      />,
      { wrapper }
    );
    await user.click(screen.getByRole("button", { name: /open lifecycle menu/i }));
    expect(await screen.findByRole("menuitem", { name: /reactivate/i })).toBeVisible();
    expect(await screen.findByRole("menuitem", { name: /restore to deprecated/i })).toBeVisible();
  });

  it("canReverse=true + lifecycle=active shows no reverse items (reverse only valid from Deprecated/Decommissioned)", () => {
    render(
      <LifecycleMenu
        application={{ ...baseApp, lifecycle: "active", sunsetDate: null }}
        canReverse={true}
        canForward={false}
      />,
      { wrapper }
    );
    // Active + canForward=false + canReverse=true → no items → badge only, no trigger.
    expect(screen.queryByRole("button", { name: /open lifecycle menu/i })).toBeNull();
  });

  it("canReverse=false + lifecycle=decommissioned shows no reverse items and no dropdown trigger", () => {
    render(
      <LifecycleMenu
        application={{ ...baseApp, lifecycle: "decommissioned", sunsetDate: PAST }}
        canReverse={false}
        canForward={false}
      />,
      { wrapper }
    );
    expect(screen.queryByRole("button", { name: /open lifecycle menu/i })).toBeNull();
    // Badge is still rendered.
    expect(screen.getByText(/decommissioned/i)).toBeInTheDocument();
  });
});
