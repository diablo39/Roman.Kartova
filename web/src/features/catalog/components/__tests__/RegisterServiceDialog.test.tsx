import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { Toaster } from "sonner";

vi.mock("sonner", async (importOriginal) => {
  const mod = await importOriginal<typeof import("sonner")>();
  return { ...mod, toast: { ...mod.toast, success: vi.fn(), error: vi.fn() } };
});
import { toast } from "sonner";

const useAuthMock = vi.fn();
vi.mock("react-oidc-context", () => ({ useAuth: () => useAuthMock() }));

const TEAMS = [
  { id: "00000000-0000-0000-0000-000000000010", displayName: "Platform", description: null },
  { id: "00000000-0000-0000-0000-000000000011", displayName: "Frontend", description: null },
];
const useTeamsListMock = vi.fn();
vi.mock("@/features/teams/api/teams", () => ({
  useTeamsList: (...args: unknown[]) => useTeamsListMock(...args),
}));
function makeTeamsResult(items: typeof TEAMS) {
  return { items, isLoading: false, isError: false, hasNext: false, hasPrev: false,
    goNext: vi.fn(), goPrev: vi.fn(), reset: vi.fn(), refetch: vi.fn(), isFetching: false, error: null };
}

const mutateAsync = vi.fn();
vi.mock("@/features/catalog/api/services", () => ({
  useRegisterService: () => ({ mutateAsync, isPending: false }),
}));

import { RegisterServiceDialog } from "../RegisterServiceDialog";

function setup({ onOpenChange = vi.fn() } = {}) {
  useAuthMock.mockReturnValue({
    isAuthenticated: true,
    user: { access_token: "tok", profile: { sub: "u-1", name: "Alice Admin", email: "alice@orga.kartova.local", tenant_id: "t" } },
  });
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={qc}>
      <Toaster />
      <RegisterServiceDialog open onOpenChange={onOpenChange} />
    </QueryClientProvider>,
  );
  return { onOpenChange };
}

describe("RegisterServiceDialog", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    useTeamsListMock.mockReturnValue(makeTeamsResult(TEAMS));
  });

  it("renders the core fields and team options", () => {
    setup();
    expect(screen.getByLabelText(/display name/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/description/i)).toBeInTheDocument();
    expect(screen.getByTestId("register-service-team-select")).toBeInTheDocument();
    expect(screen.getByRole("option", { name: "Platform" })).toBeInTheDocument();
  });

  it("blocks submit with 'Team is required' when no team chosen", async () => {
    setup();
    await userEvent.type(screen.getByLabelText(/display name/i), "Orders");
    await userEvent.type(screen.getByLabelText(/description/i), "Order service");
    await userEvent.click(screen.getByRole("button", { name: /register service/i }));
    expect(await screen.findByText("Team is required")).toBeInTheDocument();
    expect(mutateAsync).not.toHaveBeenCalled();
  });

  it("submits valid input (incl. one endpoint), toasts success, and closes", async () => {
    mutateAsync.mockResolvedValue({ id: "svc-1" });
    const onOpenChange = vi.fn();
    setup({ onOpenChange });

    await userEvent.type(screen.getByLabelText(/display name/i), "Orders");
    await userEvent.type(screen.getByLabelText(/description/i), "Order service");
    await userEvent.selectOptions(screen.getByTestId("register-service-team-select"), "Platform");
    await userEvent.click(screen.getByRole("button", { name: /add endpoint/i }));
    await userEvent.type(screen.getByLabelText(/endpoint 1 url/i), "https://api.example.com/v1");

    await userEvent.click(screen.getByRole("button", { name: /register service/i }));

    await waitFor(() =>
      expect(mutateAsync).toHaveBeenCalledWith(expect.objectContaining({
        displayName: "Orders", description: "Order service", teamId: TEAMS[0]!.id,
        endpoints: [{ url: "https://api.example.com/v1", protocol: "rest" }],
      })),
    );
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Service registered"));
    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false));
  });

  it("shows a per-row error and does not submit when an endpoint URL is invalid", async () => {
    setup();
    await userEvent.type(screen.getByLabelText(/display name/i), "Orders");
    await userEvent.type(screen.getByLabelText(/description/i), "Order service");
    await userEvent.selectOptions(screen.getByTestId("register-service-team-select"), "Platform");
    await userEvent.click(screen.getByRole("button", { name: /add endpoint/i }));
    await userEvent.type(screen.getByLabelText(/endpoint 1 url/i), "not-a-url");
    await userEvent.click(screen.getByRole("button", { name: /register service/i }));

    expect(await screen.findByText(/must be an absolute url/i)).toBeInTheDocument();
    expect(mutateAsync).not.toHaveBeenCalled();
  });

  it("maps ProblemDetails 400 field errors to the form", async () => {
    mutateAsync.mockRejectedValue({ status: 400, errors: { displayName: ["Name already taken"] } });
    setup();
    await userEvent.type(screen.getByLabelText(/display name/i), "Orders");
    await userEvent.type(screen.getByLabelText(/description/i), "Order service");
    await userEvent.selectOptions(screen.getByTestId("register-service-team-select"), "Platform");
    await userEvent.click(screen.getByRole("button", { name: /register service/i }));
    expect(await screen.findByText(/name already taken/i)).toBeInTheDocument();
  });

  it("falls back to a toast on a flat ProblemDetails error", async () => {
    mutateAsync.mockRejectedValue({ status: 422, title: "Invalid team", detail: "The supplied teamId does not resolve to a team in the current tenant." });
    const onOpenChange = vi.fn();
    setup({ onOpenChange });
    await userEvent.type(screen.getByLabelText(/display name/i), "Orders");
    await userEvent.type(screen.getByLabelText(/description/i), "Order service");
    await userEvent.selectOptions(screen.getByTestId("register-service-team-select"), "Platform");
    await userEvent.click(screen.getByRole("button", { name: /register service/i }));
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("The supplied teamId does not resolve to a team in the current tenant."));
    expect(onOpenChange).not.toHaveBeenCalledWith(false);
  });
});
