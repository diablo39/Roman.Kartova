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
}: {
  role: string;
  teamIds?: string[];
  put?: ReturnType<typeof vi.fn>;
}) {
  mockPermissions({ role, teamIds });
  const get = vi.fn().mockResolvedValue({
    data: { items: TEAMS, nextCursor: null, prevCursor: null },
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
      <AssignTeamPicker applicationId="app-1" currentTeamId={null} />
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
