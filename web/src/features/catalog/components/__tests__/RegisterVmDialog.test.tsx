import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
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

const useAuthMock = vi.fn();
vi.mock("react-oidc-context", () => ({
  useAuth: () => useAuthMock(),
}));

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

// Mock the register mutation hook — avoids apiClient spy complexity and keeps the test
// focused on the form contract (field collection + schema validation), same pattern as
// RegisterApplicationDialog.test.tsx.
const mutateAsync = vi.fn();
vi.mock("@/features/catalog/api/infrastructure", () => ({
  useRegisterVm: () => ({
    mutateAsync,
    isPending: false,
  }),
}));

import { RegisterVmDialog } from "../RegisterVmDialog";

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
      <RegisterVmDialog open={open} onOpenChange={onOpenChange} />
    </QueryClientProvider>,
  );
  return { onOpenChange };
}

describe("RegisterVmDialog", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    useTeamsListMock.mockReturnValue(makeTeamsResult(TEAMS));
  });

  // gate-7 M1: VmFormFields is generic over the caller's field-values type rather than
  // a cast between RegisterVmInput/EditVmInput Control<...>s — this guards that the
  // shared fields (displayName/description/provider/attributes.*) still render and
  // wire correctly through RegisterVmDialog's own RegisterVmInput form.
  it("renders the shared VmFormFields plus RegisterVmDialog's own team select and Created by pill", () => {
    setup();
    expect(screen.getByLabelText(/display name/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/description/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/provider/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/^os/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/hostname/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/region/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/vcpu/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/memory/i)).toBeInTheDocument();
    // InputTags renders its <Label> unassociated (no `for`/`aria-labelledby` — a
    // pre-existing gap, not introduced here), so getByLabelText can't find it; assert
    // the label text and the input via its placeholder instead.
    expect(screen.getByText("IP Addresses")).toBeInTheDocument();
    expect(screen.getByPlaceholderText(/press enter to add/i)).toBeInTheDocument();
    expect(screen.getByTestId("register-vm-team-select")).toBeInTheDocument();
    expect(screen.getByText(/alice admin/i)).toBeInTheDocument();
  });

  it("renders team options from useTeamsList", () => {
    setup();
    expect(screen.getByRole("option", { name: "Platform" })).toBeInTheDocument();
    expect(screen.getByRole("option", { name: "Frontend" })).toBeInTheDocument();
  });

  // Proves the shared VmFormFields validation wiring is live end-to-end through
  // RegisterVmDialog's own RegisterVmInput schema — the same schema-drift scenario the
  // removed Control<A> ↔ Control<B> cast (gate-7 M1) could have silently broken. Submits
  // with everything blank so client-side zod validation fires without needing to
  // exercise the team `<select>` (a separate, pre-existing field this dialog owns).
  it("rejects empty submit with shared-field validation errors", async () => {
    setup();

    await userEvent.click(screen.getByRole("button", { name: /register virtual machine/i }));

    expect(await screen.findByText(/display name must not be empty/i)).toBeInTheDocument();
    expect(screen.getByText(/description is required/i)).toBeInTheDocument();
    expect(screen.getByText(/os must not be empty/i)).toBeInTheDocument();
    expect(screen.getByText(/hostname must not be empty/i)).toBeInTheDocument();
    expect(screen.getByText(/region must not be empty/i)).toBeInTheDocument();
    expect(screen.getByText(/at least one ip address is required/i)).toBeInTheDocument();
    expect(mutateAsync).not.toHaveBeenCalled();
  });

  it("disables submit and shows hint when no teams are available", () => {
    useTeamsListMock.mockReturnValue(makeTeamsResult([]));

    setup();

    expect(screen.getByRole("button", { name: /register virtual machine/i })).toBeDisabled();
    expect(
      screen.getByText("No teams available — create a team first before registering a virtual machine."),
    ).toBeInTheDocument();
  });
});
