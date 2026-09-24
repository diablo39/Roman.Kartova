import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { Toaster } from "sonner";

import * as clientModule from "@/features/catalog/api/client";
import { EditEnvironmentDialog } from "../EditEnvironmentDialog";
import type { EnvironmentDetailResponse } from "@/features/catalog/api/environments";

const baseEnv: EnvironmentDetailResponse = {
  id: "00000000-0000-0000-0000-000000000abc",
  tenantId: "t1",
  displayName: "Prod EU",
  description: "Primary prod",
  type: "production",
  region: "eu-west-1",
  resourceDetails: {},
  createdByUserId: "u1",
  createdAt: "2026-04-30T00:00:00Z",
  version: "v1",
};

function setup({
  put,
  environment = baseEnv,
  open = true,
  onOpenChange = vi.fn(),
}: {
  put: ReturnType<typeof vi.fn>;
  environment?: EnvironmentDetailResponse;
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
      <EditEnvironmentDialog environment={environment} open={open} onOpenChange={onOpenChange} />
    </QueryClientProvider>,
  );
  return { onOpenChange, qc };
}

describe("EditEnvironmentDialog", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it("pre-fills the form from the environment prop", () => {
    setup({ put: vi.fn() });
    expect(screen.getByLabelText(/display name/i)).toHaveValue("Prod EU");
    expect(screen.getByLabelText(/description/i)).toHaveValue("Primary prod");
    expect(screen.getByLabelText(/region/i)).toHaveValue("eu-west-1");
    expect(screen.getByLabelText(/type/i)).toHaveValue("production");
  });

  it("submits PUT with If-Match header derived from the version and closes on success", async () => {
    const put = vi.fn().mockResolvedValue({
      data: { ...baseEnv, displayName: "Prod EU Renamed", version: "v2" },
      error: undefined,
      response: { status: 200 } as Response,
    });
    const onOpenChange = vi.fn();
    setup({ put, onOpenChange });

    const display = screen.getByLabelText(/display name/i);
    await userEvent.clear(display);
    await userEvent.type(display, "Prod EU Renamed");

    await userEvent.click(screen.getByRole("button", { name: /save changes/i }));

    await waitFor(() => expect(put).toHaveBeenCalled());
    expect(put).toHaveBeenCalledWith(
      "/api/v1/catalog/environments/{id}",
      expect.objectContaining({
        params: { path: { id: baseEnv.id } },
        headers: { "If-Match": '"v1"' },
        body: expect.objectContaining({
          displayName: "Prod EU Renamed",
          type: "production",
          region: "eu-west-1",
        }),
      }),
    );
    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false));
  });

  it("sends region: null when the field is cleared", async () => {
    const put = vi.fn().mockResolvedValue({
      data: { ...baseEnv, region: null },
      error: undefined,
      response: { status: 200 } as Response,
    });
    setup({ put });

    const region = screen.getByLabelText(/region/i);
    await userEvent.clear(region);

    await userEvent.click(screen.getByRole("button", { name: /save changes/i }));

    await waitFor(() => expect(put).toHaveBeenCalled());
    expect(put).toHaveBeenCalledWith(
      "/api/v1/catalog/environments/{id}",
      expect.objectContaining({
        body: expect.objectContaining({ region: null }),
      }),
    );
  });

  it("on 409 EnvironmentNameConflict sets the displayName field error and keeps the dialog open", async () => {
    const put = vi.fn().mockResolvedValue({
      data: undefined,
      error: {
        type: "https://kartova.io/problems/environment-name-conflict",
        title: "Environment name already in use",
        detail: "An environment named 'Staging' already exists in this tenant.",
      },
      response: { status: 409 } as Response,
    });
    const onOpenChange = vi.fn();
    setup({ put, onOpenChange });

    await userEvent.click(screen.getByRole("button", { name: /save changes/i }));

    expect(await screen.findByText(/already exists in this tenant/i)).toBeInTheDocument();
    expect(onOpenChange).not.toHaveBeenCalledWith(false);
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
        expect.objectContaining({ queryKey: ["environments", "detail", baseEnv.id] }),
      ),
    );
  });

  it("on 400 ProblemDetails with a displayName error key sets the field error", async () => {
    const put = vi.fn().mockResolvedValue({
      data: undefined,
      error: { status: 400, errors: { displayName: ["Environment display name must not be empty."] } },
      response: { status: 400 } as Response,
    });
    const onOpenChange = vi.fn();
    setup({ put, onOpenChange });

    await userEvent.click(screen.getByRole("button", { name: /save changes/i }));

    expect(await screen.findByText(/must not be empty/i)).toBeInTheDocument();
    expect(onOpenChange).not.toHaveBeenCalledWith(false);
  });
});
