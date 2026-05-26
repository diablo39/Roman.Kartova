import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { Toaster } from "sonner";

import * as clientModule from "@/features/catalog/api/client";
import { DeleteTeamConfirmDialog } from "../DeleteTeamConfirmDialog";

const team = {
  id: "00000000-0000-0000-0000-000000000abc",
  displayName: "Platform",
  description: "",
  createdAt: "2026-01-01T00:00:00Z",
};

function setup({
  del,
  onOpenChange = vi.fn(),
}: {
  del: ReturnType<typeof vi.fn>;
  onOpenChange?: (b: boolean) => void;
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
      <DeleteTeamConfirmDialog team={team} open={true} onOpenChange={onOpenChange} />
    </QueryClientProvider>,
  );
  return { onOpenChange };
}

describe("DeleteTeamConfirmDialog", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it("happy path: clicks Delete, calls DELETE and closes the dialog", async () => {
    const del = vi.fn().mockResolvedValue({
      data: undefined, error: undefined, response: { status: 204 },
    });
    const onOpenChange = vi.fn();
    setup({ del, onOpenChange });

    await userEvent.click(screen.getByRole("button", { name: /delete team/i }));

    await waitFor(() => expect(del).toHaveBeenCalled());
    expect(del).toHaveBeenCalledWith(
      "/api/v1/organizations/teams/{id}",
      { params: { path: { id: team.id } } },
    );
    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false));
  });

  it("409 path: toasts the application count and keeps dialog open", async () => {
    const del = vi.fn().mockResolvedValue({
      data: undefined,
      error: { title: "Conflict", applicationCount: 3 },
      response: { status: 409 },
    });
    const onOpenChange = vi.fn();
    setup({ del, onOpenChange });

    await userEvent.click(screen.getByRole("button", { name: /delete team/i }));

    await waitFor(() =>
      expect(screen.getByText(/3 application\(s\)/i)).toBeInTheDocument(),
    );
    expect(onOpenChange).not.toHaveBeenCalledWith(false);
  });
});
