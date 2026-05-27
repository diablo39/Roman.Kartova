import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { Toaster } from "sonner";

import * as clientModule from "@/features/catalog/api/client";

const useAuthMock = vi.fn();
vi.mock("react-oidc-context", () => ({
  useAuth: () => useAuthMock(),
}));

import { RegisterApplicationDialog } from "../RegisterApplicationDialog";

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
  } as never);

  useAuthMock.mockReturnValue({
    isAuthenticated: true,
    user: {
      access_token: "tok",
      profile: {
        sub: "u-1",
        name: "Alice Admin",
        email: "alice@orga.kartova.local",
        tenant_id: "t",
      },
    },
  });

  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={qc}>
      <Toaster />
      <RegisterApplicationDialog open={open} onOpenChange={onOpenChange} />
    </QueryClientProvider>
  );
  return { onOpenChange };
}

describe("RegisterApplicationDialog", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it("renders Display Name, Description fields and the Owner pill", () => {
    setup({ post: vi.fn() });
    expect(screen.getByLabelText(/display name/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/description/i)).toBeInTheDocument();
    expect(screen.getByText(/alice admin/i)).toBeInTheDocument();
    expect(screen.getByText(/active/i)).toBeInTheDocument();
  });

  it("rejects empty submit with field-level error messages", async () => {
    const post = vi.fn();
    setup({ post });
    await userEvent.click(screen.getByRole("button", { name: /register application/i }));
    expect(await screen.findByText(/display name must not be empty/i)).toBeInTheDocument();
    expect(post).not.toHaveBeenCalled();
  });

  it("submits valid input and closes on 201", async () => {
    const post = vi.fn().mockResolvedValue({
      data: { id: "00000000-0000-0000-0000-000000000001", displayName: "P", description: "d" },
      error: undefined,
    });
    const onOpenChange = vi.fn();
    setup({ post, onOpenChange });

    await userEvent.type(screen.getByLabelText(/display name/i), "Payment Gateway");
    await userEvent.type(screen.getByLabelText(/description/i), "Handles charges");
    await userEvent.click(screen.getByRole("button", { name: /register application/i }));

    await waitFor(() => expect(post).toHaveBeenCalled());
    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false));
  });

  it("maps ProblemDetails 400 errors to fields when payload has errors map", async () => {
    const post = vi.fn().mockResolvedValue({
      data: undefined,
      error: { status: 400, errors: { displayName: ["Display name already taken"] } },
    });
    setup({ post });

    await userEvent.type(screen.getByLabelText(/display name/i), "Payment Gateway");
    await userEvent.type(screen.getByLabelText(/description/i), "Handles charges");
    await userEvent.click(screen.getByRole("button", { name: /register application/i }));

    expect(await screen.findByText(/display name already taken/i)).toBeInTheDocument();
  });

  it("falls back to a toast when 400 has no errors map (flat ProblemDetails)", async () => {
    const post = vi.fn().mockResolvedValue({
      data: undefined,
      error: { status: 400, title: "Validation failed", detail: "Application display name must not be empty." },
    });
    const onOpenChange = vi.fn();
    setup({ post, onOpenChange });

    await userEvent.type(screen.getByLabelText(/display name/i), "Payment Gateway");
    await userEvent.type(screen.getByLabelText(/description/i), "Handles charges");
    await userEvent.click(screen.getByRole("button", { name: /register application/i }));

    // Toast renders via sonner — assert detail text appears anywhere on screen.
    await waitFor(() =>
      expect(screen.getByText(/Application display name must not be empty/i)).toBeInTheDocument()
    );
    // Dialog should remain open for retry on flat-ProblemDetails errors.
    expect(onOpenChange).not.toHaveBeenCalledWith(false);
  });
});
