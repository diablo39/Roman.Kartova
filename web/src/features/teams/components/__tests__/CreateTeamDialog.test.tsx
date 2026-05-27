import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { Toaster } from "sonner";

import * as clientModule from "@/features/catalog/api/client";
import { CreateTeamDialog } from "../CreateTeamDialog";

function setup({
  post,
  open = true,
  onOpenChange = vi.fn(),
}: {
  post: ReturnType<typeof vi.fn>;
  open?: boolean;
  onOpenChange?: (b: boolean) => void;
}) {
  vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
    GET: vi.fn(),
    POST: post,
    PUT: vi.fn(),
    DELETE: vi.fn(),
  } as never);

  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={qc}>
      <Toaster />
      <CreateTeamDialog open={open} onOpenChange={onOpenChange} />
    </QueryClientProvider>,
  );
  return { onOpenChange };
}

describe("CreateTeamDialog", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it("rejects empty displayName with a zod-min validation error", async () => {
    const post = vi.fn();
    setup({ post });

    await userEvent.click(screen.getByRole("button", { name: /^create team$/i }));
    expect(await screen.findByText(/display name is required/i)).toBeInTheDocument();
    expect(post).not.toHaveBeenCalled();
  });

  it("submits valid input, calls the mutation, toasts success and closes", async () => {
    const post = vi.fn().mockResolvedValue({
      data: { id: "t-new", displayName: "Ops", description: "", createdAt: "2026-01-01T00:00:00Z" },
      error: undefined,
      response: { status: 201 },
    });
    const onOpenChange = vi.fn();
    setup({ post, onOpenChange });

    await userEvent.type(screen.getByLabelText(/display name/i), "Ops");
    await userEvent.click(screen.getByRole("button", { name: /^create team$/i }));

    await waitFor(() => expect(post).toHaveBeenCalled());
    expect(post).toHaveBeenCalledWith(
      "/api/v1/organizations/teams",
      expect.objectContaining({
        body: expect.objectContaining({ displayName: "Ops" }),
      }),
    );
    await waitFor(() => expect(screen.getByText(/team created/i)).toBeInTheDocument());
    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false));
  });
});
