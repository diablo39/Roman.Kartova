import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { Toaster } from "sonner";

import * as clientModule from "@/features/catalog/api/client";
import { SetSuccessorDialog } from "../SetSuccessorDialog";
import type { ApplicationResponse } from "@/features/catalog/api/applications";

const baseApp: ApplicationResponse = {
  id: "00000000-0000-0000-0000-000000000abc",
  tenantId: "t1",
  displayName: "Payments",
  description: "Payments service.",
  createdByUserId: "u1",
  createdAt: "2026-04-30T00:00:00Z",
  lifecycle: "deprecated",
  sunsetDate: "2099-01-01T00:00:00Z",
  teamId: "team-1",
  version: "v1",
  successorApplicationId: null,
  successorDisplayName: null,
};

const otherApp = {
  kind: "application" as const,
  id: "00000000-0000-0000-0000-000000000def",
  displayName: "Payments v2",
};

vi.mock("@/features/catalog/api/relationships", () => ({
  useEntitySearch: () => ({ data: [otherApp], isLoading: false, isError: false }),
}));

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
      <SetSuccessorDialog application={application} open={open} onOpenChange={onOpenChange} />
    </QueryClientProvider>
  );
  return { onOpenChange };
}

describe("SetSuccessorDialog", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it("selecting an application PUTs {successorApplicationId} and closes on success", async () => {
    const put = vi.fn().mockResolvedValue({
      data: { ...baseApp, successorApplicationId: otherApp.id, successorDisplayName: otherApp.displayName },
      error: undefined,
      response: { status: 200 } as Response,
    });
    const onOpenChange = vi.fn();
    setup({ put, onOpenChange });

    await userEvent.type(screen.getByRole("combobox"), "Payments v2");
    await waitFor(() => expect(screen.getByText("Payments v2")).toBeInTheDocument());
    await userEvent.click(screen.getByText("Payments v2"));

    await waitFor(() => expect(put).toHaveBeenCalled());
    expect(put).toHaveBeenCalledWith(
      "/api/v1/catalog/applications/{id}/successor",
      expect.objectContaining({
        params: { path: { id: baseApp.id } },
        body: { successorApplicationId: otherApp.id },
      })
    );
    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false));
  });

  it("Clear PUTs {successorApplicationId:null} when a successor is currently set", async () => {
    const put = vi.fn().mockResolvedValue({
      data: { ...baseApp, successorApplicationId: null, successorDisplayName: null },
      error: undefined,
      response: { status: 200 } as Response,
    });
    const onOpenChange = vi.fn();
    setup({
      put,
      onOpenChange,
      application: {
        ...baseApp,
        successorApplicationId: otherApp.id,
        successorDisplayName: otherApp.displayName,
      },
    });

    await userEvent.click(screen.getByRole("button", { name: /clear/i }));

    await waitFor(() => expect(put).toHaveBeenCalled());
    expect(put).toHaveBeenCalledWith(
      "/api/v1/catalog/applications/{id}/successor",
      expect.objectContaining({
        params: { path: { id: baseApp.id } },
        body: { successorApplicationId: null },
      })
    );
    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false));
  });

  it("on 422 invalid-successor toasts and stays open", async () => {
    const put = vi.fn().mockResolvedValue({
      data: undefined,
      error: {
        type: "https://kartova.io/problems/invalid-successor",
        title: "Invalid successor",
        detail: "That application can't be set as a successor.",
      },
      response: { status: 422 } as Response,
    });
    const onOpenChange = vi.fn();
    setup({ put, onOpenChange });

    await userEvent.type(screen.getByRole("combobox"), "Payments v2");
    await waitFor(() => expect(screen.getByText("Payments v2")).toBeInTheDocument());
    await userEvent.click(screen.getByText("Payments v2"));

    await waitFor(() =>
      expect(screen.getByText(/can't be set as a successor/i)).toBeInTheDocument()
    );
    expect(onOpenChange).not.toHaveBeenCalledWith(false);
  });
});
