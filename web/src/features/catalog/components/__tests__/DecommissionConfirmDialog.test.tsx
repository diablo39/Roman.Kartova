import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { Toaster } from "sonner";

import * as clientModule from "@/features/catalog/api/client";
import { DecommissionConfirmDialog } from "../DecommissionConfirmDialog";
import type { ApplicationResponse } from "@/features/catalog/api/applications";

const baseApp: ApplicationResponse = {
  id: "00000000-0000-0000-0000-000000000abc",
  tenantId: "t1",
  name: "payments",
  displayName: "Payments",
  description: "Payments service.",
  ownerUserId: "u1",
  createdAt: "2026-04-30T00:00:00Z",
  lifecycle: "deprecated",
  sunsetDate: "2026-01-01T00:00:00Z",
  version: "v1",
};

function setup({
  post,
  application = baseApp,
  open = true,
  onOpenChange = vi.fn(),
}: {
  post: ReturnType<typeof vi.fn>;
  application?: ApplicationResponse;
  open?: boolean;
  onOpenChange?: (b: boolean) => void;
}) {
  vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
    GET: vi.fn(),
    POST: post,
    PUT: vi.fn(),
  } as never);

  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={qc}>
      <Toaster />
      <DecommissionConfirmDialog application={application} open={open} onOpenChange={onOpenChange} />
    </QueryClientProvider>
  );
  return { onOpenChange };
}

describe("DecommissionConfirmDialog", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it("renders the terminal-state warning copy", () => {
    setup({ post: vi.fn() });
    expect(screen.getByText(/terminal state/i)).toBeInTheDocument();
    expect(screen.getByText(/cannot be undone/i)).toBeInTheDocument();
  });

  it("submits empty POST /decommission on confirm and closes on success", async () => {
    const post = vi.fn().mockResolvedValue({
      data: { ...baseApp, lifecycle: "decommissioned" },
      error: undefined,
      response: { status: 200 } as Response,
    });
    const onOpenChange = vi.fn();
    setup({ post, onOpenChange });

    await userEvent.click(screen.getByRole("button", { name: /^decommission$/i }));

    await waitFor(() => expect(post).toHaveBeenCalled());
    expect(post).toHaveBeenCalledWith(
      "/api/v1/catalog/applications/{id}/decommission",
      expect.objectContaining({ params: { path: { id: baseApp.id } } })
    );
    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false));
  });

  it('on 409 with reason="before-sunset-date" toasts the formatted date and closes', async () => {
    const post = vi.fn().mockResolvedValue({
      data: undefined,
      error: {
        type: "https://kartova.io/problems/lifecycle-conflict",
        title: "Lifecycle transition not allowed",
        currentLifecycle: "deprecated",
        attemptedTransition: "Decommission",
        reason: "before-sunset-date",
        sunsetDate: "2099-06-15T00:00:00Z",
      },
      response: { status: 409 } as Response,
    });
    const onOpenChange = vi.fn();
    setup({ post, onOpenChange });

    await userEvent.click(screen.getByRole("button", { name: /^decommission$/i }));

    await waitFor(() => expect(post).toHaveBeenCalled());
    // Toast must include "before sunset date" wording AND a localized rendering
    // of 2099-06-15. The exact display format is locale-dependent so we match
    // a substring rather than a fixed string.
    await waitFor(() =>
      expect(screen.getByText(/before sunset date/i)).toBeInTheDocument()
    );
    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false));
  });

  it("on 409 generic toasts the friendly current state label and closes", async () => {
    const post = vi.fn().mockResolvedValue({
      data: undefined,
      error: {
        type: "https://kartova.io/problems/lifecycle-conflict",
        title: "Lifecycle transition not allowed",
        currentLifecycle: "active",
        attemptedTransition: "Decommission",
      },
      response: { status: 409 } as Response,
    });
    const onOpenChange = vi.fn();
    setup({ post, onOpenChange });

    await userEvent.click(screen.getByRole("button", { name: /^decommission$/i }));

    await waitFor(() => expect(post).toHaveBeenCalled());
    // Wire "active" → friendly "Active" via lifecycleLabel().
    await waitFor(() =>
      expect(screen.getByText(/current state is Active/)).toBeInTheDocument()
    );
    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false));
  });

  it("falls back to a toast when the error is unrecognised", async () => {
    const post = vi.fn().mockResolvedValue({
      data: undefined,
      error: { status: 500, title: "Bang", detail: "Server exploded." },
      response: { status: 500 } as Response,
    });
    const onOpenChange = vi.fn();
    setup({ post, onOpenChange });

    await userEvent.click(screen.getByRole("button", { name: /^decommission$/i }));

    await waitFor(() => expect(screen.getByText(/server exploded/i)).toBeInTheDocument());
    expect(onOpenChange).not.toHaveBeenCalledWith(false);
  });
});
