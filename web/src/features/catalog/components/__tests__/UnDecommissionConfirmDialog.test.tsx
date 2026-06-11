import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { Toaster } from "sonner";

import * as clientModule from "@/features/catalog/api/client";

import { UnDecommissionConfirmDialog } from "../UnDecommissionConfirmDialog";
import type { ApplicationResponse } from "@/features/catalog/api/applications";

const baseApp: ApplicationResponse = {
  id: "00000000-0000-0000-0000-000000000abc",
  tenantId: "t1",
  displayName: "Payments",
  description: "Payments service.",
  createdByUserId: "u1",
  createdAt: "2026-04-30T00:00:00Z",
  lifecycle: "decommissioned",
  sunsetDate: "2026-01-01T00:00:00Z",
  teamId: "team-1",
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
    GET: vi.fn(), POST: post,
  } as never);

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
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("renders the form with a pre-filled future sunset date input (today + 30d)", () => {
    setup({ post: vi.fn() });

    const sunset = screen.getByLabelText(/sunset date/i) as HTMLInputElement;
    expect(sunset.value).toMatch(/^\d{4}-\d{2}-\d{2}$/);
    // Pre-fill is "today + 30d at UTC midnight"; assert it's strictly future.
    expect(new Date(sunset.value).getTime()).toBeGreaterThan(Date.now());
  });

  it("renders the restore-to-deprecated copy", () => {
    setup({ post: vi.fn() });
    // Heading includes "Restore ... to Deprecated?"
    expect(screen.getByRole("heading", { level: 2 })).toHaveTextContent(/restore/i);
    expect(screen.getByText(/provide a new future sunset date/i)).toBeInTheDocument();
  });

  it("form rejects a past sunsetDate with a zod refine error", async () => {
    setup({ post: vi.fn() });

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
    const post = vi.fn().mockResolvedValue({
      data: successApp,
      error: undefined,
      response: { status: 200 },
    });
    const onOpenChange = vi.fn();
    setup({ post, onOpenChange });

    // Replace the pre-filled date with a known future date.
    const sunset = screen.getByLabelText(/sunset date/i) as HTMLInputElement;
    await userEvent.clear(sunset);
    await userEvent.type(sunset, futureDateStr);

    await userEvent.click(screen.getByRole("button", { name: /restore to deprecated/i }));

    await waitFor(() => expect(post).toHaveBeenCalled());
    const [url, opts] = post.mock.calls[0] as [string, { params: { path: { id: string } }; body: { sunsetDate: string } }];
    expect(url).toContain(`/un-decommission`);
    expect(opts.body.sunsetDate).toMatch(/^\d{4}-\d{2}-\d{2}T00:00:00\.000Z$/);

    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false));
  });

  it("Cancel closes the dialog without calling POST", async () => {
    const post = vi.fn();
    const onOpenChange = vi.fn();
    setup({ post, onOpenChange });

    await userEvent.click(screen.getByRole("button", { name: /cancel/i }));

    expect(post).not.toHaveBeenCalled();
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it("on 409 LifecycleConflict toasts the friendly current state label and closes", async () => {
    const futureDate = new Date(Date.now() + 60 * 24 * 60 * 60 * 1000);
    const futureDateStr = futureDate.toISOString().slice(0, 10);
    const problem = {
      type: "https://kartova.io/problems/lifecycle-conflict",
      title: "Lifecycle transition not allowed",
      currentLifecycle: "deprecated",
      attemptedTransition: "UnDecommission",
    };
    const post = vi.fn().mockResolvedValue({
      data: undefined,
      error: problem,
      response: { status: 409 },
    });
    const onOpenChange = vi.fn();
    setup({ post, onOpenChange });

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
    const problem = { status: 500, title: "Bang", detail: "Server exploded." };
    const post = vi.fn().mockResolvedValue({
      data: undefined,
      error: problem,
      response: { status: 500 },
    });
    const onOpenChange = vi.fn();
    setup({ post, onOpenChange });

    const sunset = screen.getByLabelText(/sunset date/i) as HTMLInputElement;
    await userEvent.clear(sunset);
    await userEvent.type(sunset, futureDateStr);

    await userEvent.click(screen.getByRole("button", { name: /restore to deprecated/i }));

    await waitFor(() => expect(screen.getByText(/server exploded/i)).toBeInTheDocument());
    expect(onOpenChange).not.toHaveBeenCalledWith(false);
  });
});
