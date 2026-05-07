import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { Toaster } from "sonner";

import * as clientModule from "@/features/catalog/api/client";
import { DeprecateConfirmDialog } from "../DeprecateConfirmDialog";
import type { ApplicationResponse } from "@/features/catalog/api/applications";

const baseApp: ApplicationResponse = {
  id: "00000000-0000-0000-0000-000000000abc",
  tenantId: "t1",
  name: "payments",
  displayName: "Payments",
  description: "Payments service.",
  ownerUserId: "u1",
  createdAt: "2026-04-30T00:00:00Z",
  lifecycle: "active",
  sunsetDate: null,
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
      <DeprecateConfirmDialog application={application} open={open} onOpenChange={onOpenChange} />
    </QueryClientProvider>
  );
  return { onOpenChange };
}

describe("DeprecateConfirmDialog", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it("renders the sunset date input pre-filled with a future date (today + 30d)", () => {
    setup({ post: vi.fn() });

    const sunset = screen.getByLabelText(/sunset date/i) as HTMLInputElement;
    expect(sunset.value).toMatch(/^\d{4}-\d{2}-\d{2}$/);
    // Pre-fill is "today + 30d at UTC midnight"; assert it's strictly future.
    expect(new Date(sunset.value).getTime()).toBeGreaterThan(Date.now());
  });

  it("submits POST /deprecate with the sunsetDate and closes on success", async () => {
    const post = vi.fn().mockResolvedValue({
      data: { ...baseApp, lifecycle: "deprecated", sunsetDate: "2099-01-01T00:00:00Z" },
      error: undefined,
      response: { status: 200 } as Response,
    });
    const onOpenChange = vi.fn();
    setup({ post, onOpenChange });

    await userEvent.click(screen.getByRole("button", { name: /^deprecate$/i }));

    await waitFor(() => expect(post).toHaveBeenCalled());
    expect(post).toHaveBeenCalledWith(
      "/api/v1/catalog/applications/{id}/deprecate",
      expect.objectContaining({
        params: { path: { id: baseApp.id } },
        body: expect.objectContaining({
          sunsetDate: expect.stringMatching(/^\d{4}-\d{2}-\d{2}T00:00:00\.000Z$/),
        }),
      })
    );
    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false));
  });

  it("on 409 LifecycleConflict toasts the friendly current state label and closes", async () => {
    const post = vi.fn().mockResolvedValue({
      data: undefined,
      error: {
        type: "https://kartova.io/problems/lifecycle-conflict",
        title: "Lifecycle transition not allowed",
        currentLifecycle: "deprecated",
        attemptedTransition: "Deprecate",
      },
      response: { status: 409 } as Response,
    });
    const onOpenChange = vi.fn();
    setup({ post, onOpenChange });

    await userEvent.click(screen.getByRole("button", { name: /^deprecate$/i }));

    await waitFor(() => expect(post).toHaveBeenCalled());
    // Wire shape is "deprecated" (lowercase); the toast renders the friendly
    // "Deprecated" label via lifecycleLabel() — pinning the helper integration.
    await waitFor(() =>
      expect(screen.getByText(/current state is Deprecated/)).toBeInTheDocument()
    );
    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false));
  });

  it("on 400 ProblemDetails with errors map sets field-level errors and stays open", async () => {
    const post = vi.fn().mockResolvedValue({
      data: undefined,
      error: { status: 400, errors: { sunsetDate: ["sunsetDate must be in the future."] } },
      response: { status: 400 } as Response,
    });
    const onOpenChange = vi.fn();
    setup({ post, onOpenChange });

    await userEvent.click(screen.getByRole("button", { name: /^deprecate$/i }));

    expect(await screen.findByText(/sunsetDate must be in the future/i)).toBeInTheDocument();
    expect(onOpenChange).not.toHaveBeenCalledWith(false);
  });

  it("falls back to a toast when the error is unrecognised", async () => {
    const post = vi.fn().mockResolvedValue({
      data: undefined,
      error: { status: 500, title: "Bang", detail: "Server exploded." },
      response: { status: 500 } as Response,
    });
    const onOpenChange = vi.fn();
    setup({ post, onOpenChange });

    await userEvent.click(screen.getByRole("button", { name: /^deprecate$/i }));

    await waitFor(() => expect(screen.getByText(/server exploded/i)).toBeInTheDocument());
    expect(onOpenChange).not.toHaveBeenCalledWith(false);
  });
});
