import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { Toaster } from "sonner";

// Mock react-oidc-context before importing components that use useAuth.
const useAuthMock = vi.fn();
vi.mock("react-oidc-context", () => ({
  useAuth: () => useAuthMock(),
}));

import { ReactivateConfirmDialog } from "../ReactivateConfirmDialog";
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
  fetchImpl,
  application = baseApp,
  open = true,
  onOpenChange = vi.fn(),
}: {
  fetchImpl: typeof globalThis.fetch;
  application?: ApplicationResponse;
  open?: boolean;
  onOpenChange?: (b: boolean) => void;
}) {
  globalThis.fetch = fetchImpl;

  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={qc}>
      <Toaster />
      <ReactivateConfirmDialog application={application} open={open} onOpenChange={onOpenChange} />
    </QueryClientProvider>
  );
  return { onOpenChange };
}

describe("ReactivateConfirmDialog", () => {
  let originalFetch: typeof globalThis.fetch;

  beforeEach(() => {
    originalFetch = globalThis.fetch;
    useAuthMock.mockReturnValue({
      isAuthenticated: true,
      user: { access_token: "test-token" },
    });
  });

  afterEach(() => {
    globalThis.fetch = originalFetch;
    vi.restoreAllMocks();
    useAuthMock.mockReset();
  });

  it("renders the reactivate confirmation copy", () => {
    setup({ fetchImpl: vi.fn() });
    expect(screen.getByText(/returns to/i)).toBeInTheDocument();
    expect(screen.getByText(/active/i)).toBeInTheDocument();
    expect(screen.getByText(/sunset date is cleared/i)).toBeInTheDocument();
  });

  it("calls POST /reactivate on confirm and closes on success", async () => {
    const successApp = { ...baseApp, lifecycle: "active" as const, sunsetDate: null };
    const fetchImpl = vi.fn().mockResolvedValue(
      new Response(JSON.stringify(successApp), {
        status: 200,
        headers: { "Content-Type": "application/json" },
      })
    );
    const onOpenChange = vi.fn();
    setup({ fetchImpl, onOpenChange });

    await userEvent.click(screen.getByRole("button", { name: /^reactivate$/i }));

    await waitFor(() => expect(fetchImpl).toHaveBeenCalled());
    const [url, opts] = fetchImpl.mock.calls[0] as [string, RequestInit];
    expect(url).toContain(`/reactivate`);
    expect(opts.method).toBe("POST");
    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false));
  });

  it("includes Authorization header with bearer token", async () => {
    const successApp = { ...baseApp, lifecycle: "active" as const, sunsetDate: null };
    const fetchImpl = vi.fn().mockResolvedValue(
      new Response(JSON.stringify(successApp), {
        status: 200,
        headers: { "Content-Type": "application/json" },
      })
    );
    setup({ fetchImpl });

    await userEvent.click(screen.getByRole("button", { name: /^reactivate$/i }));

    await waitFor(() => expect(fetchImpl).toHaveBeenCalled());
    const [, opts] = fetchImpl.mock.calls[0] as [string, RequestInit];
    const headers = opts.headers as Record<string, string>;
    expect(headers.Authorization).toBe("Bearer test-token");
  });

  it("Cancel closes the dialog without calling fetch", async () => {
    const fetchImpl = vi.fn();
    const onOpenChange = vi.fn();
    setup({ fetchImpl, onOpenChange });

    await userEvent.click(screen.getByRole("button", { name: /cancel/i }));

    expect(fetchImpl).not.toHaveBeenCalled();
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it("on 409 LifecycleConflict toasts the friendly current state label and closes", async () => {
    const fetchImpl = vi.fn().mockResolvedValue(
      new Response(
        JSON.stringify({
          type: "https://kartova.io/problems/lifecycle-conflict",
          title: "Lifecycle transition not allowed",
          currentLifecycle: "active",
          attemptedTransition: "Reactivate",
        }),
        { status: 409, headers: { "Content-Type": "application/json" } }
      )
    );
    const onOpenChange = vi.fn();
    setup({ fetchImpl, onOpenChange });

    await userEvent.click(screen.getByRole("button", { name: /^reactivate$/i }));

    await waitFor(() =>
      expect(screen.getByText(/current state is Active/)).toBeInTheDocument()
    );
    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false));
  });

  it("falls back to a toast when the error is unrecognised and dialog stays open", async () => {
    const fetchImpl = vi.fn().mockResolvedValue(
      new Response(
        JSON.stringify({ status: 500, title: "Bang", detail: "Server exploded." }),
        { status: 500, headers: { "Content-Type": "application/json" } }
      )
    );
    const onOpenChange = vi.fn();
    setup({ fetchImpl, onOpenChange });

    await userEvent.click(screen.getByRole("button", { name: /^reactivate$/i }));

    await waitFor(() => expect(screen.getByText(/server exploded/i)).toBeInTheDocument());
    expect(onOpenChange).not.toHaveBeenCalledWith(false);
  });
});
