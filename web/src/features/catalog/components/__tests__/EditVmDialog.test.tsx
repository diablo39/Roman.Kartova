import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { Toaster } from "sonner";

import * as clientModule from "@/features/catalog/api/client";
import { EditVmDialog } from "../EditVmDialog";
import type { VmDetailResponse } from "@/features/catalog/api/infrastructure";

const baseVm: VmDetailResponse = {
  id: "00000000-0000-0000-0000-000000000abc",
  tenantId: "t1",
  displayName: "web-prod-01",
  description: "Web server",
  provider: "AWS",
  teamId: "team-1",
  systemId: null,
  createdByUserId: "u1",
  createdAt: "2026-04-30T00:00:00Z",
  version: "v1",
  attributes: {
    powerState: "running",
    os: "Ubuntu 24.04",
    vcpu: 2,
    memoryGb: 4,
    hostname: "web-prod-01.internal",
    ipAddresses: ["10.0.0.1"],
    region: "eu-west-1",
  },
};

function setup({
  put,
  vm = baseVm,
  open = true,
  onOpenChange = vi.fn(),
}: {
  put: ReturnType<typeof vi.fn>;
  vm?: VmDetailResponse;
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
      <EditVmDialog vm={vm} open={open} onOpenChange={onOpenChange} />
    </QueryClientProvider>,
  );
  return { onOpenChange, qc };
}

describe("EditVmDialog", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it("pre-fills the form from the vm prop, including provider and numeric attributes as strings", () => {
    setup({ put: vi.fn() });
    expect(screen.getByLabelText(/display name/i)).toHaveValue("web-prod-01");
    expect(screen.getByLabelText(/description/i)).toHaveValue("Web server");
    expect(screen.getByLabelText(/provider/i)).toHaveValue("AWS");
    expect(screen.getByLabelText(/^os/i)).toHaveValue("Ubuntu 24.04");
    expect(screen.getByLabelText(/hostname/i)).toHaveValue("web-prod-01.internal");
    expect(screen.getByLabelText(/vcpu/i)).toHaveValue(2);
    expect(screen.getByLabelText(/memory/i)).toHaveValue(4);
  });

  it("submits PUT with If-Match header derived from the version and closes on success", async () => {
    const put = vi.fn().mockResolvedValue({
      data: { ...baseVm, displayName: "web-prod-02", version: "v2" },
      error: undefined,
      response: { status: 200 } as Response,
    });
    const onOpenChange = vi.fn();
    setup({ put, onOpenChange });

    const display = screen.getByLabelText(/display name/i);
    await userEvent.clear(display);
    await userEvent.type(display, "web-prod-02");

    await userEvent.click(screen.getByRole("button", { name: /save changes/i }));

    await waitFor(() => expect(put).toHaveBeenCalled());
    expect(put).toHaveBeenCalledWith(
      "/api/v1/catalog/infrastructure/vms/{id}",
      expect.objectContaining({
        params: { path: { id: baseVm.id } },
        headers: { "If-Match": '"v1"' },
        body: expect.objectContaining({
          displayName: "web-prod-02",
          provider: "AWS",
        }),
      }),
    );
    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false));
  });

  it("sends provider: null when the field is cleared", async () => {
    const put = vi.fn().mockResolvedValue({
      data: { ...baseVm, provider: null },
      error: undefined,
      response: { status: 200 } as Response,
    });
    setup({ put });

    const provider = screen.getByLabelText(/provider/i);
    await userEvent.clear(provider);

    await userEvent.click(screen.getByRole("button", { name: /save changes/i }));

    await waitFor(() => expect(put).toHaveBeenCalled());
    expect(put).toHaveBeenCalledWith(
      "/api/v1/catalog/infrastructure/vms/{id}",
      expect.objectContaining({
        body: expect.objectContaining({ provider: null }),
      }),
    );
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
      expect(screen.getByText(/someone else edited this/i)).toBeInTheDocument(),
    );
    expect(onOpenChange).not.toHaveBeenCalledWith(false);
    await waitFor(() =>
      expect(invalidateSpy).toHaveBeenCalledWith(
        expect.objectContaining({ queryKey: ["infrastructure", "vms", "detail", baseVm.id] }),
      ),
    );
  });

  // gate-7 C1: the real 400 shape for a VM-attribute validation failure keys on the
  // dotted SPA form-field path (VmAttributes.Validate's ParamName), e.g.
  // "attributes.os" — not a fictitious top-level "displayName" mock that never
  // exercised the bug (the errors map used to key on "dto" and render invisibly).
  it("on 400 ProblemDetails with a nested attributes.* error key sets the attribute field error", async () => {
    const put = vi.fn().mockResolvedValue({
      data: undefined,
      error: { status: 400, errors: { "attributes.os": ["Os must not be empty."] } },
      response: { status: 400 } as Response,
    });
    const onOpenChange = vi.fn();
    setup({ put, onOpenChange });

    await userEvent.click(screen.getByRole("button", { name: /save changes/i }));

    expect(await screen.findByText(/os must not be empty/i)).toBeInTheDocument();
    expect(onOpenChange).not.toHaveBeenCalledWith(false);
  });

  // TD-003: a 400 whose error key maps to no registered field must surface a toast, never vanish.
  it("on 400 with only an unmapped error key shows a toast and keeps the dialog open", async () => {
    const put = vi.fn().mockResolvedValue({
      data: undefined,
      error: { status: 400, detail: "Server rejected the change.", errors: { serverOnlyKey: ["nope"] } },
      response: { status: 400 } as Response,
    });
    const onOpenChange = vi.fn();
    setup({ put, onOpenChange });

    await userEvent.click(screen.getByRole("button", { name: /save changes/i }));

    expect(await screen.findByText(/server rejected the change/i)).toBeInTheDocument();
    expect(onOpenChange).not.toHaveBeenCalledWith(false);
  });

  // TD-003 mixed-key case (the one a naive OR-fold swallowed): a mapped field error is shown
  // AND the unmapped key still surfaces a toast, rather than being silently dropped.
  it("on 400 mixing a mapped field error with an unmapped key shows both the field error and a toast", async () => {
    const put = vi.fn().mockResolvedValue({
      data: undefined,
      error: {
        status: 400,
        detail: "Server rejected the change.",
        errors: { "attributes.os": ["Os must not be empty."], serverOnlyKey: ["nope"] },
      },
      response: { status: 400 } as Response,
    });
    const onOpenChange = vi.fn();
    setup({ put, onOpenChange });

    await userEvent.click(screen.getByRole("button", { name: /save changes/i }));

    expect(await screen.findByText(/os must not be empty/i)).toBeInTheDocument();
    expect(await screen.findByText(/server rejected the change/i)).toBeInTheDocument();
    expect(onOpenChange).not.toHaveBeenCalledWith(false);
  });
});
