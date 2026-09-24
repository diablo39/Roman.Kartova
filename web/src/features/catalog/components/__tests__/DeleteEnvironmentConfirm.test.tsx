import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { Toaster } from "sonner";

import * as clientModule from "@/features/catalog/api/client";
import { DeleteEnvironmentConfirm } from "../DeleteEnvironmentConfirm";
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
  del,
  onOpenChange = vi.fn(),
  onDeleted = vi.fn(),
}: {
  del: ReturnType<typeof vi.fn>;
  onOpenChange?: (b: boolean) => void;
  onDeleted?: () => void;
}) {
  vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
    GET: vi.fn(),
    POST: vi.fn(),
    PUT: vi.fn(),
    DELETE: del,
  } as never);

  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={qc}>
      <Toaster />
      <DeleteEnvironmentConfirm environment={baseEnv} open={true} onOpenChange={onOpenChange} onDeleted={onDeleted} />
    </QueryClientProvider>,
  );
  return { onOpenChange, onDeleted };
}

describe("DeleteEnvironmentConfirm", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it("renders as a react-aria dialog, not a native confirm/alert", () => {
    const confirmSpy = vi.spyOn(window, "confirm");
    const alertSpy = vi.spyOn(window, "alert");
    setup({ del: vi.fn() });

    expect(screen.getByRole("dialog", { name: /delete environment/i })).toBeInTheDocument();
    expect(confirmSpy).not.toHaveBeenCalled();
    expect(alertSpy).not.toHaveBeenCalled();
  });

  it("happy path: clicks Delete, sends DELETE with If-Match derived from version, closes and calls onDeleted", async () => {
    const del = vi.fn().mockResolvedValue({
      data: undefined, error: undefined, response: { status: 204 },
    });
    const { onOpenChange, onDeleted } = setup({ del });

    await userEvent.click(screen.getByRole("button", { name: /delete environment/i }));

    await waitFor(() => expect(del).toHaveBeenCalled());
    expect(del).toHaveBeenCalledWith(
      "/api/v1/catalog/environments/{id}",
      {
        params: { path: { id: baseEnv.id } },
        headers: { "If-Match": '"v1"' },
      },
    );
    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false));
    expect(onDeleted).toHaveBeenCalled();
  });

  it("412 path: toasts a reload message, keeps dialog open, and does not call onDeleted", async () => {
    const del = vi.fn().mockResolvedValue({
      data: undefined,
      error: { type: "https://kartova.io/problems/concurrency-conflict", title: "stale" },
      response: { status: 412 },
    });
    const { onOpenChange, onDeleted } = setup({ del });

    await userEvent.click(screen.getByRole("button", { name: /delete environment/i }));

    await waitFor(() =>
      expect(screen.getByText(/someone else changed this environment/i)).toBeInTheDocument(),
    );
    expect(onOpenChange).not.toHaveBeenCalledWith(false);
    expect(onDeleted).not.toHaveBeenCalled();
  });
});
