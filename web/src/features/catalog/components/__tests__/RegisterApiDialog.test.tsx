import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

const mutateAsync = vi.fn().mockResolvedValue({ id: "new-api" });
vi.mock("@/features/catalog/api/apis", () => ({ useRegisterApi: () => ({ mutateAsync, isPending: false }) }));
vi.mock("@/features/teams/api/teams", () => ({
  useTeamsList: () => ({ items: [{ id: "team1", displayName: "Platform" }], isLoading: false, isError: false }),
}));
vi.mock("@/shared/auth/useCurrentUser", () => ({ useCurrentUser: () => ({ displayName: "Dev", email: "d@x.io" }) }));
vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

import { RegisterApiDialog } from "../RegisterApiDialog";

beforeEach(() => mutateAsync.mockClear());

describe("RegisterApiDialog", () => {
  it("submits displayName/description/style/version/teamId", async () => {
    const user = userEvent.setup();
    render(<RegisterApiDialog open onOpenChange={vi.fn()} />);
    await user.type(screen.getByLabelText(/Display Name/i), "Orders API");
    await user.type(screen.getByLabelText(/Description/i), "Order mgmt");
    await user.type(screen.getByLabelText(/Version/i), "v1");
    await user.selectOptions(screen.getByTestId("register-api-team-select"), "team1");
    await user.click(screen.getByRole("button", { name: /Register API/i }));
    await waitFor(() =>
      expect(mutateAsync).toHaveBeenCalledWith(expect.objectContaining({
        displayName: "Orders API", description: "Order mgmt", version: "v1", teamId: "team1", style: "rest",
      })),
    );
  });
});
