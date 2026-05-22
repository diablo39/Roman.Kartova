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

import { UnDecommissionConfirmDialog } from "../UnDecommissionConfirmDialog";
import type { ApplicationResponse } from "@/features/catalog/api/applications";

const baseApp: ApplicationResponse = {
  id: "00000000-0000-0000-0000-000000000abc",
  tenantId: "t1",
  name: "payments",
  displayName: "Payments",
  description: "Payments service.",
  ownerUserId: "u1",
  createdAt: "2026-04-30T00:00:00Z",
  lifecycle: "decommissioned",
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
      <UnDecommissionConfirmDialog application={application} open={open} onOpenChange={onOpenChange} />
    </QueryClientProvider>
  );
  return { onOpenChange };
}

describe("UnDecommissionConfirmDialog", () => {
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

  it("renders the form with a pre-filled future sunset date input (today + 30d)", () => {
    setup({ fetchImpl: vi.fn() });

    const sunset = screen.getByLabelText(/sunset date/i) as HTMLInputElement;
    expect(sunset.value).toMatch(/^\d{4}-\d{2}-\d{2}$/);
    // Pre-fill is "today + 30d at UTC midnight"; assert it's strictly future.
    expect(new Date(sunset.value).getTime()).toBeGreaterThan(Date.now());
  });

  it("renders the restore-to-deprecated copy", () => {
    setup({ fetchImpl: vi.fn() });
    // Heading includes "Restore ... to Deprecated?"
    expect(screen.getByRole("heading", { level: 2 })).toHaveTextContent(/restore/i);
    expect(screen.getByText(/provide a new future sunset date/i)).toBeInTheDocument();
  });

  it("form rejects a past sunsetDate with a zod refine error", async () => {
    setup({ fetchImpl: vi.fn() });

    // Clear the pre-filled value and enter a past date.
    const sunset = screen.getByLabelText(/sunset date/i) as HTMLInputElement;
    await userEvent.clear(sunset);
    await userEvent.type(sunset, "2000-01-01");

    await userEvent.click(screen.getByRole("button", { name: /restore to deprecated/i }));

    expect(await screen.findByText(/must be in the future/i)).toBeInTheDocument();
  });

  it("submits POST /un-decommission with sunsetDate and closes on success", async () => {
    const futureDate = new Date(Date.now() + 60 * 24 * 60 * 60 * 1000);
    const futureDateStr = futureDate.toISOString().slice(0, 10); // YYYY-MM-DD

    const successApp = {
      ...baseApp,
      lifecycle: "deprecated" as const,
      sunsetDate: `${futureDateStr}T00:00:00.000Z`,
    };
    const fetchImpl = vi.fn().mockResolvedValue(
      new Response(JSON.stringify(successApp), {
        status: 200,
        headers: { "Content-Type": "application/json" },
      })
    );
    const onOpenChange = vi.fn();
    setup({ fetchImpl, onOpenChange });

    // Replace the pre-filled date with a known future date.
    const sunset = screen.getByLabelText(/sunset date/i) as HTMLInputElement;
    await userEvent.clear(sunset);
    await userEvent.type(sunset, futureDateStr);

    await userEvent.click(screen.getByRole("button", { name: /restore to deprecated/i }));

    await waitFor(() => expect(fetchImpl).toHaveBeenCalled());
    const [url, opts] = fetchImpl.mock.calls[0] as [string, RequestInit];
    expect(url).toContain(`/un-decommission`);
    expect(opts.method).toBe("POST");

    const body = JSON.parse(opts.body as string) as { sunsetDate: string };
    expect(body.sunsetDate).toMatch(/^\d{4}-\d{2}-\d{2}T00:00:00\.000Z$/);

    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false));
  });

  it("includes Authorization header with bearer token", async () => {
    const futureDate = new Date(Date.now() + 60 * 24 * 60 * 60 * 1000);
    const futureDateStr = futureDate.toISOString().slice(0, 10);
    const successApp = { ...baseApp, lifecycle: "deprecated" as const };
    const fetchImpl = vi.fn().mockResolvedValue(
      new Response(JSON.stringify(successApp), {
        status: 200,
        headers: { "Content-Type": "application/json" },
      })
    );
    setup({ fetchImpl });

    const sunset = screen.getByLabelText(/sunset date/i) as HTMLInputElement;
    await userEvent.clear(sunset);
    await userEvent.type(sunset, futureDateStr);

    await userEvent.click(screen.getByRole("button", { name: /restore to deprecated/i }));

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
    const futureDate = new Date(Date.now() + 60 * 24 * 60 * 60 * 1000);
    const futureDateStr = futureDate.toISOString().slice(0, 10);
    const fetchImpl = vi.fn().mockResolvedValue(
      new Response(
        JSON.stringify({
          type: "https://kartova.io/problems/lifecycle-conflict",
          title: "Lifecycle transition not allowed",
          currentLifecycle: "deprecated",
          attemptedTransition: "UnDecommission",
        }),
        { status: 409, headers: { "Content-Type": "application/json" } }
      )
    );
    const onOpenChange = vi.fn();
    setup({ fetchImpl, onOpenChange });

    const sunset = screen.getByLabelText(/sunset date/i) as HTMLInputElement;
    await userEvent.clear(sunset);
    await userEvent.type(sunset, futureDateStr);

    await userEvent.click(screen.getByRole("button", { name: /restore to deprecated/i }));

    await waitFor(() =>
      expect(screen.getByText(/current state is Deprecated/)).toBeInTheDocument()
    );
    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false));
  });

  it("falls back to a toast when the error is unrecognised and dialog stays open", async () => {
    const futureDate = new Date(Date.now() + 60 * 24 * 60 * 60 * 1000);
    const futureDateStr = futureDate.toISOString().slice(0, 10);
    const fetchImpl = vi.fn().mockResolvedValue(
      new Response(
        JSON.stringify({ status: 500, title: "Bang", detail: "Server exploded." }),
        { status: 500, headers: { "Content-Type": "application/json" } }
      )
    );
    const onOpenChange = vi.fn();
    setup({ fetchImpl, onOpenChange });

    const sunset = screen.getByLabelText(/sunset date/i) as HTMLInputElement;
    await userEvent.clear(sunset);
    await userEvent.type(sunset, futureDateStr);

    await userEvent.click(screen.getByRole("button", { name: /restore to deprecated/i }));

    await waitFor(() => expect(screen.getByText(/server exploded/i)).toBeInTheDocument());
    expect(onOpenChange).not.toHaveBeenCalledWith(false);
  });
});
