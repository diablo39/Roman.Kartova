import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { Toaster } from "sonner";

import * as clientModule from "@/features/catalog/api/client";
import { AssignTeamPicker } from "../AssignTeamPicker";

const usePermissionsMock = vi.fn();
vi.mock("@/shared/auth/usePermissions", () => ({
  usePermissions: () => usePermissionsMock(),
}));

import { KartovaPermissions } from "@/shared/auth/permissions";

function mockPermissions(p: { role: string; teamIds?: string[] }) {
  usePermissionsMock.mockReturnValue({
    role: p.role,
    teamIds: p.teamIds ?? [],
    teamAdminTeamIds: [],
    hasPermission: (perm: string) => perm === KartovaPermissions.CatalogApplicationsEditMetadata,
    isLoading: false,
  });
}

const TEAMS = [
  { id: "t-1", displayName: "Alpha", description: "", createdAt: "2026-01-01T00:00:00Z" },
  { id: "t-2", displayName: "Beta", description: "", createdAt: "2026-01-01T00:00:00Z" },
];

function setup({
  role,
  teamIds,
  put,
  currentTeamId = null,
  teams = TEAMS,
}: {
  role: string;
  teamIds?: string[];
  put?: ReturnType<typeof vi.fn>;
  currentTeamId?: string | null;
  teams?: typeof TEAMS;
}) {
  mockPermissions({ role, teamIds });
  const get = vi.fn().mockResolvedValue({
    data: { items: teams, nextCursor: null, prevCursor: null },
    error: undefined,
  });
  vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
    GET: get,
    POST: vi.fn(),
    PUT: put ?? vi.fn(),
    DELETE: vi.fn(),
  } as never);

  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={qc}>
      <Toaster />
      <AssignTeamPicker applicationId="app-1" currentTeamId={currentTeamId} />
    </QueryClientProvider>,
  );
  return { get };
}

describe("AssignTeamPicker", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it("for OrgAdmin: lists every team from useTeamsList", async () => {
    setup({ role: "OrgAdmin" });

    const select = (await screen.findByLabelText(/team:/i)) as HTMLSelectElement;
    await waitFor(() => expect(within(select).getByText("Alpha")).toBeInTheDocument());
    expect(within(select).getByText("Beta")).toBeInTheDocument();
  });

  it("for Member: lists only teams the user belongs to", async () => {
    setup({ role: "Member", teamIds: ["t-2"] });

    const select = (await screen.findByLabelText(/team:/i)) as HTMLSelectElement;
    await waitFor(() => expect(within(select).getByText("Beta")).toBeInTheDocument());
    expect(within(select).queryByText("Alpha")).toBeNull();
  });

  it("for Member with no membership on the application's current team: picker is disabled (mirrors API 403)", async () => {
    setup({ role: "Member", teamIds: ["t-other"], currentTeamId: "t-1" });

    const select = (await screen.findByLabelText(/team:/i)) as HTMLSelectElement;
    await waitFor(() => expect(select).toBeDisabled());
  });

  it("for OrgAdmin: picker is enabled even when currentTeamId is null", async () => {
    setup({ role: "OrgAdmin", currentTeamId: null });

    const select = (await screen.findByLabelText(/team:/i)) as HTMLSelectElement;
    await waitFor(() => expect(within(select).getByText("Alpha")).toBeInTheDocument());
    expect(select).not.toBeDisabled();
  });

  it("for Member who is a member of the application's current team: picker is enabled", async () => {
    setup({ role: "Member", teamIds: ["t-1"], currentTeamId: "t-1" });

    const select = (await screen.findByLabelText(/team:/i)) as HTMLSelectElement;
    await waitFor(() => expect(within(select).getByText("Alpha")).toBeInTheDocument());
    expect(select).not.toBeDisabled();
  });

  it("injects a synthetic option when currentTeamId is not in the visible team list (e.g. >200 teams)", async () => {
    // OrgAdmin sees only the truncated page of teams; current team is outside it.
    setup({
      role: "OrgAdmin",
      currentTeamId: "t-missing",
      teams: [{ id: "t-other", displayName: "Other", description: "", createdAt: "2026-01-01T00:00:00Z" }],
    });

    const select = (await screen.findByLabelText(/team:/i)) as HTMLSelectElement;
    // The controlled value resolves to a real <option>, so React doesn't warn
    // and the select doesn't visually blank.
    await waitFor(() => expect(select.value).toBe("t-missing"));
  });

  it("changing the selection calls the assign mutation with the new teamId", async () => {
    const put = vi.fn().mockResolvedValue({
      data: { id: "app-1", teamId: "t-1" }, error: undefined, response: { status: 200 },
    });
    setup({ role: "OrgAdmin", put });

    const select = (await screen.findByLabelText(/team:/i)) as HTMLSelectElement;
    await waitFor(() => expect(within(select).getByText("Alpha")).toBeInTheDocument());

    await userEvent.selectOptions(select, "t-1");

    await waitFor(() => expect(put).toHaveBeenCalled());
    expect(put).toHaveBeenCalledWith(
      "/api/v1/catalog/applications/{id}/team",
      expect.objectContaining({
        params: { path: { id: "app-1" } },
        body: { teamId: "t-1" },
      }),
    );
  });
});
