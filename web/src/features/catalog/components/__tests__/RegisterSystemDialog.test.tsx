import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

vi.mock("react-oidc-context", () => ({
  useAuth: () => ({ isAuthenticated: true, user: { access_token: "t", profile: { sub: "u", name: "Alice", email: "a@x", tenant_id: "t" } } }),
}));

vi.mock("sonner", async (importOriginal) => {
  const mod = await importOriginal<typeof import("sonner")>();
  return { ...mod, toast: { ...mod.toast, success: vi.fn(), error: vi.fn() } };
});
import { toast } from "sonner";

const mutateAsync = vi.fn().mockResolvedValue({ id: "s1" });
vi.mock("@/features/catalog/api/systems", () => ({ useRegisterSystem: () => ({ mutateAsync, isPending: false }) }));

const useTeamsListMock = vi.fn();
vi.mock("@/features/teams/api/teams", () => ({ useTeamsList: () => useTeamsListMock() }));

import { RegisterSystemDialog } from "../RegisterSystemDialog";

const TEAM_ID = "11111111-1111-1111-1111-111111111111";
function teamsResult(items: { id: string; displayName: string }[]) {
  return { items, isLoading: false, isFetching: false, isError: false, error: null, hasNext: false, hasPrev: false, goNext: vi.fn(), goPrev: vi.fn(), reset: vi.fn(), refetch: vi.fn() };
}

describe("RegisterSystemDialog", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    useTeamsListMock.mockReturnValue(teamsResult([{ id: TEAM_ID, displayName: "Platform Team" }]));
  });

  it("submits the payload and calls onOpenChange(false) on success", async () => {
    const onOpenChange = vi.fn();
    render(<RegisterSystemDialog open onOpenChange={onOpenChange} />);

    await userEvent.type(screen.getByLabelText(/Display Name/i), "Payments");
    await userEvent.selectOptions(screen.getByTestId("register-system-team-select"), TEAM_ID);
    await userEvent.click(screen.getByRole("button", { name: /Register System/i }));

    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false));
    expect(mutateAsync).toHaveBeenCalledWith(expect.objectContaining({ displayName: "Payments", teamId: TEAM_ID }));
  });

  it("blocks submit and shows an error when no team is selected", async () => {
    render(<RegisterSystemDialog open onOpenChange={vi.fn()} />);
    await userEvent.type(screen.getByLabelText(/Display Name/i), "Payments");
    await userEvent.click(screen.getByRole("button", { name: /Register System/i }));
    expect(await screen.findByText("Team is required")).toBeInTheDocument();
    expect(mutateAsync).not.toHaveBeenCalled();
  });

  it("disables submit and hints when no teams exist", () => {
    useTeamsListMock.mockReturnValue(teamsResult([]));
    render(<RegisterSystemDialog open onOpenChange={vi.fn()} />);
    expect(screen.getByRole("button", { name: /Register System/i })).toBeDisabled();
    expect(screen.getByText(/create a team first/i)).toBeInTheDocument();
  });

  it("maps a ProblemDetails field error to the form and keeps the dialog open", async () => {
    mutateAsync.mockRejectedValueOnce({ status: 409, errors: { displayName: ["taken"] } });
    const onOpenChange = vi.fn();
    render(<RegisterSystemDialog open onOpenChange={onOpenChange} />);

    await userEvent.type(screen.getByLabelText(/Display Name/i), "Payments");
    await userEvent.selectOptions(screen.getByTestId("register-system-team-select"), TEAM_ID);
    await userEvent.click(screen.getByRole("button", { name: /Register System/i }));

    expect(await screen.findByText("taken")).toBeInTheDocument();
    expect(onOpenChange).not.toHaveBeenCalledWith(false);
  });

  it("toasts on a flat (non-field) ProblemDetails error and keeps the dialog open", async () => {
    mutateAsync.mockRejectedValueOnce({ status: 422, detail: "boom" });
    const onOpenChange = vi.fn();
    render(<RegisterSystemDialog open onOpenChange={onOpenChange} />);

    await userEvent.type(screen.getByLabelText(/Display Name/i), "Payments");
    await userEvent.selectOptions(screen.getByTestId("register-system-team-select"), TEAM_ID);
    await userEvent.click(screen.getByRole("button", { name: /Register System/i }));

    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("boom"));
    expect(onOpenChange).not.toHaveBeenCalledWith(false);
  });

  it("clears the selected team and validation error on close, then reopen", async () => {
    const onOpenChange = vi.fn();
    const { rerender } = render(<RegisterSystemDialog open onOpenChange={onOpenChange} />);

    await userEvent.type(screen.getByLabelText(/Display Name/i), "Payments");
    await userEvent.click(screen.getByRole("button", { name: /Register System/i }));
    expect(await screen.findByText("Team is required")).toBeInTheDocument();

    await userEvent.selectOptions(screen.getByTestId("register-system-team-select"), TEAM_ID);
    expect(screen.queryByText("Team is required")).toBeNull();

    rerender(<RegisterSystemDialog open={false} onOpenChange={onOpenChange} />);
    rerender(<RegisterSystemDialog open onOpenChange={onOpenChange} />);

    expect(screen.getByTestId("register-system-team-select")).toHaveValue("");
    expect(screen.queryByText("Team is required")).toBeNull();
  });
});
