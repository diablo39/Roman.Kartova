import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { Toaster } from "sonner";

vi.mock("sonner", async (importOriginal) => {
  const mod = await importOriginal<typeof import("sonner")>();
  return {
    ...mod,
    toast: {
      ...mod.toast,
      success: vi.fn(),
      error: vi.fn(),
    },
  };
});

import { toast } from "sonner";

const useAuthMock = vi.fn();
vi.mock("react-oidc-context", () => ({
  useAuth: () => useAuthMock(),
}));

// Mock useTeamsList so the team picker is populated without an API call.
const TEAMS = [
  { id: "00000000-0000-0000-0000-000000000010", displayName: "Platform", description: null },
  { id: "00000000-0000-0000-0000-000000000011", displayName: "Frontend", description: null },
];

const useTeamsListMock = vi.fn();
vi.mock("@/features/teams/api/teams", () => ({
  useTeamsList: (...args: unknown[]) => useTeamsListMock(...args),
}));

function makeTeamsResult(items: typeof TEAMS) {
  return {
    items,
    isLoading: false,
    isError: false,
    hasNext: false,
    hasPrev: false,
    goNext: vi.fn(),
    goPrev: vi.fn(),
    reset: vi.fn(),
    refetch: vi.fn(),
    isFetching: false,
    error: null,
  };
}

// Mock the register mutation hook — avoids apiClient spy complexity and keeps
// the test focused on the form contract (field collection + schema validation).
const mutateAsync = vi.fn();
vi.mock("@/features/catalog/api/applications", () => ({
  useRegisterApplication: () => ({
    mutateAsync,
    isPending: false,
  }),
}));

import { RegisterApplicationDialog } from "../RegisterApplicationDialog";

function setup({
  open = true,
  onOpenChange = vi.fn(),
}: {
  open?: boolean;
  onOpenChange?: (b: boolean) => void;
} = {}) {
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
    vi.clearAllMocks();
    useTeamsListMock.mockReturnValue(makeTeamsResult(TEAMS));
  });

  it("renders Display Name, Description, Team fields and the Created by pill", () => {
    setup();
    expect(screen.getByLabelText(/display name/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/description/i)).toBeInTheDocument();
    expect(screen.getByTestId("register-team-select")).toBeInTheDocument();
    expect(screen.getByText(/alice admin/i)).toBeInTheDocument();
    expect(screen.getByText(/active/i)).toBeInTheDocument();
  });

  it("renders team options from useTeamsList", () => {
    setup();
    expect(screen.getByRole("option", { name: "Platform" })).toBeInTheDocument();
    expect(screen.getByRole("option", { name: "Frontend" })).toBeInTheDocument();
  });

  it("rejects empty submit with field-level error messages", async () => {
    setup();
    await userEvent.click(screen.getByRole("button", { name: /register application/i }));
    expect(await screen.findByText(/display name must not be empty/i)).toBeInTheDocument();
    expect(mutateAsync).not.toHaveBeenCalled();
  });

  it("submits valid input including teamId, calls mutateAsync with all fields, toasts success, and closes", async () => {
    mutateAsync.mockResolvedValue({ id: "00000000-0000-0000-0000-000000000001" });
    const onOpenChange = vi.fn();
    setup({ onOpenChange });

    await userEvent.type(screen.getByLabelText(/display name/i), "Payment Gateway");
    await userEvent.type(screen.getByLabelText(/description/i), "Handles charges");
    await userEvent.selectOptions(screen.getByTestId("register-team-select"), "Platform");

    await userEvent.click(screen.getByRole("button", { name: /register application/i }));

    await waitFor(() =>
      expect(mutateAsync).toHaveBeenCalledWith(
        expect.objectContaining({
          displayName: "Payment Gateway",
          description: "Handles charges",
          teamId: TEAMS[0].id,
        }),
      ),
    );
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Application registered"));
    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false));
  });

  it("maps ProblemDetails 400 errors to fields when payload has errors map", async () => {
    mutateAsync.mockRejectedValue({
      status: 400,
      errors: { displayName: ["Display name already taken"] },
    });
    setup();

    await userEvent.type(screen.getByLabelText(/display name/i), "Payment Gateway");
    await userEvent.type(screen.getByLabelText(/description/i), "Handles charges");
    await userEvent.selectOptions(screen.getByTestId("register-team-select"), "Platform");
    await userEvent.click(screen.getByRole("button", { name: /register application/i }));

    expect(await screen.findByText(/display name already taken/i)).toBeInTheDocument();
  });

  it("shows 'Team is required' and does not call mutateAsync when team is not selected", async () => {
    setup();
    await userEvent.type(screen.getByLabelText(/display name/i), "Payment Gateway");
    await userEvent.type(screen.getByLabelText(/description/i), "Handles charges");
    // Leave team select at its empty/placeholder value — do NOT call selectOptions.
    await userEvent.click(screen.getByRole("button", { name: /register application/i }));

    expect(await screen.findByText("Team is required")).toBeInTheDocument();
    expect(mutateAsync).not.toHaveBeenCalled();
  });

  it("disables submit and shows hint when no teams are available", () => {
    // Override useTeamsList to return an empty list for this test only.
    useTeamsListMock.mockReturnValue(makeTeamsResult([]));

    setup();

    expect(screen.getByRole("button", { name: /register application/i })).toBeDisabled();
    expect(
      screen.getByText(
        "No teams available — create a team first before registering an application.",
      ),
    ).toBeInTheDocument();
  });

  it("falls back to a toast when mutation rejects with flat ProblemDetails", async () => {
    mutateAsync.mockRejectedValue({
      status: 400,
      title: "Validation failed",
      detail: "Application display name must not be empty.",
    });
    const onOpenChange = vi.fn();
    setup({ onOpenChange });

    await userEvent.type(screen.getByLabelText(/display name/i), "Payment Gateway");
    await userEvent.type(screen.getByLabelText(/description/i), "Handles charges");
    await userEvent.selectOptions(screen.getByTestId("register-team-select"), "Platform");
    await userEvent.click(screen.getByRole("button", { name: /register application/i }));

    await waitFor(() =>
      expect(toast.error).toHaveBeenCalledWith("Application display name must not be empty."),
    );
    // Dialog should remain open for retry on flat-ProblemDetails errors.
    expect(onOpenChange).not.toHaveBeenCalledWith(false);
  });
});
