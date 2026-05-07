import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { Toaster } from "sonner";

import * as clientModule from "@/features/catalog/api/client";
import { EditApplicationDialog } from "../EditApplicationDialog";
import type { ApplicationResponse } from "@/features/catalog/api/applications";

// Lifecycle wire shape is lowercase ("active" | "deprecated" |
// "decommissioned") via JsonStringEnumConverter(JsonNamingPolicy.CamelCase).
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
  put,
  application = baseApp,
  open = true,
  onOpenChange = vi.fn(),
}: {
  put: ReturnType<typeof vi.fn>;
  application?: ApplicationResponse;
  open?: boolean;
  onOpenChange?: (b: boolean) => void;
}) {
  vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
    GET: vi.fn(),
    POST: vi.fn(),
    PUT: put,
  } as never);

  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={qc}>
      <Toaster />
      <EditApplicationDialog application={application} open={open} onOpenChange={onOpenChange} />
    </QueryClientProvider>
  );
  return { onOpenChange, qc };
}

describe("EditApplicationDialog", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it("pre-fills the form from the application prop", () => {
    setup({ put: vi.fn() });
    expect(screen.getByLabelText(/display name/i)).toHaveValue("Payments");
    expect(screen.getByLabelText(/description/i)).toHaveValue("Payments service.");
  });

  it("submits PUT with If-Match header derived from the version and closes on success", async () => {
    const put = vi.fn().mockResolvedValue({
      data: { ...baseApp, displayName: "Payments v2", description: "Updated.", version: "v2" },
      error: undefined,
      response: { status: 200 } as Response,
    });
    const onOpenChange = vi.fn();
    setup({ put, onOpenChange });

    const display = screen.getByLabelText(/display name/i);
    await userEvent.clear(display);
    await userEvent.type(display, "Payments v2");

    const description = screen.getByLabelText(/description/i);
    await userEvent.clear(description);
    await userEvent.type(description, "Updated.");

    await userEvent.click(screen.getByRole("button", { name: /save changes/i }));

    await waitFor(() => expect(put).toHaveBeenCalled());
    expect(put).toHaveBeenCalledWith(
      "/api/v1/catalog/applications/{id}",
      expect.objectContaining({
        params: { path: { id: baseApp.id } },
        body: { displayName: "Payments v2", description: "Updated." },
        headers: { "If-Match": '"v1"' },
      })
    );
    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false));
  });

  it("rejects empty submit with field-level error messages and does not call PUT", async () => {
    const put = vi.fn();
    setup({
      put,
      // Use a fixture with empty values so client-side validation kicks in.
      application: { ...baseApp, displayName: "", description: "" },
    });

    await userEvent.click(screen.getByRole("button", { name: /save changes/i }));

    expect(await screen.findByText(/display name is required/i)).toBeInTheDocument();
    expect(put).not.toHaveBeenCalled();
  });

  it("on 412 ConcurrencyConflict keeps the dialog open, toasts, and invalidates the detail query", async () => {
    const put = vi.fn().mockResolvedValue({
      data: undefined,
      error: { type: "https://kartova.io/problems/concurrency-conflict", title: "stale" },
      response: { status: 412 } as Response,
    });
    const onOpenChange = vi.fn();
    const { qc } = setup({ put, onOpenChange });
    const invalidateSpy = vi.spyOn(qc, "invalidateQueries");

    await userEvent.click(screen.getByRole("button", { name: /save changes/i }));

    await waitFor(() => expect(put).toHaveBeenCalled());
    await waitFor(() =>
      expect(screen.getByText(/someone else edited this/i)).toBeInTheDocument()
    );
    // Spec §8.3: dialog stays open AND detail query is invalidated so the
    // parent page refetches and the form auto-resets via RHF `values`.
    expect(onOpenChange).not.toHaveBeenCalledWith(false);
    await waitFor(() =>
      expect(invalidateSpy).toHaveBeenCalledWith(
        expect.objectContaining({ queryKey: ["applications", "detail", baseApp.id] })
      )
    );
  });

  it("on 409 LifecycleConflict (decommissioned) closes the dialog and toasts", async () => {
    const put = vi.fn().mockResolvedValue({
      data: undefined,
      error: { type: "https://kartova.io/problems/lifecycle-conflict", title: "decommissioned" },
      response: { status: 409 } as Response,
    });
    const onOpenChange = vi.fn();
    setup({ put, onOpenChange });

    await userEvent.click(screen.getByRole("button", { name: /save changes/i }));

    await waitFor(() => expect(put).toHaveBeenCalled());
    await waitFor(() =>
      expect(screen.getByText(/decommissioned and can no longer be edited/i)).toBeInTheDocument()
    );
    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false));
  });

  it("on 400 ProblemDetails with errors map sets field-level errors", async () => {
    const put = vi.fn().mockResolvedValue({
      data: undefined,
      error: { status: 400, errors: { displayName: ["Display name is reserved"] } },
      response: { status: 400 } as Response,
    });
    const onOpenChange = vi.fn();
    setup({ put, onOpenChange });

    await userEvent.click(screen.getByRole("button", { name: /save changes/i }));

    expect(await screen.findByText(/display name is reserved/i)).toBeInTheDocument();
    expect(onOpenChange).not.toHaveBeenCalledWith(false);
  });

  it("falls back to a toast when the error is unrecognised (no errors map, no known status)", async () => {
    const put = vi.fn().mockResolvedValue({
      data: undefined,
      error: { status: 500, title: "Bang", detail: "Server exploded." },
      response: { status: 500 } as Response,
    });
    const onOpenChange = vi.fn();
    setup({ put, onOpenChange });

    await userEvent.click(screen.getByRole("button", { name: /save changes/i }));

    await waitFor(() => expect(screen.getByText(/server exploded/i)).toBeInTheDocument());
    expect(onOpenChange).not.toHaveBeenCalledWith(false);
  });
});
