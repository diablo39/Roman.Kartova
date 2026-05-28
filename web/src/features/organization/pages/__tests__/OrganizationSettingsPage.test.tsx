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

const usePermissionsMock = vi.fn();
vi.mock("@/shared/auth/usePermissions", () => ({
  usePermissions: () => usePermissionsMock(),
}));

import { OrganizationSettingsPage } from "../OrganizationSettingsPage";
import { KartovaPermissions } from "@/shared/auth/permissions";

const PROFILE = {
  id: "00000000-0000-0000-0000-000000000001",
  displayName: "Acme Corp",
  description: "An engineering org",
  defaultTimeZone: "Europe/Oslo",
  logoEtag: null,
  logoMimeType: null,
  createdAt: "2026-01-01T00:00:00Z",
};

function harness(qc: QueryClient) {
  return (
    <QueryClientProvider client={qc}>
      <Toaster />
      <OrganizationSettingsPage />
    </QueryClientProvider>
  );
}

function newQc(): QueryClient {
  return new QueryClient({
    defaultOptions: { queries: { retry: false, gcTime: 0 } },
  });
}

function mockPermissions(opts: { canEdit: boolean; loading?: boolean }) {
  usePermissionsMock.mockReturnValue({
    role: opts.canEdit ? "OrgAdmin" : "Member",
    hasPermission: (perm: string) =>
      opts.canEdit && perm === KartovaPermissions.OrgProfileEdit,
    isLoading: opts.loading ?? false,
    isError: false,
    teamIds: [],
    teamAdminTeamIds: [],
  });
}

function mockApi(opts: {
  profile?: typeof PROFILE | undefined;
  profileError?: { status: number; title?: string } | undefined;
  put?: ReturnType<typeof vi.fn>;
}) {
  const get = vi.fn().mockResolvedValue(
    opts.profileError
      ? { data: undefined, error: opts.profileError }
      : { data: opts.profile ?? PROFILE, error: undefined },
  );
  vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
    GET: get,
    POST: vi.fn(),
    PUT: opts.put ?? vi.fn(),
    DELETE: vi.fn(),
  } as never);
  return { get };
}

describe("OrganizationSettingsPage", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    useAuthMock.mockReset();
    useAuthMock.mockReturnValue({
      isAuthenticated: true,
      user: { access_token: "tok-1" },
    });
    usePermissionsMock.mockReset();
  });

  it("renders 'Loading…' while the profile query is pending", () => {
    // A GET that never resolves keeps the query in pending state.
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
      GET: vi.fn().mockReturnValue(new Promise(() => {})),
      POST: vi.fn(),
      PUT: vi.fn(),
      DELETE: vi.fn(),
    } as never);
    mockPermissions({ canEdit: true });
    render(harness(newQc()));
    expect(screen.getByText(/loading/i)).toBeInTheDocument();
  });

  it("renders an error card when the profile query errors", async () => {
    mockApi({ profileError: { status: 500, title: "boom" } });
    mockPermissions({ canEdit: true });
    render(harness(newQc()));
    await waitFor(() =>
      expect(screen.getByText(/failed to load organization profile/i)).toBeInTheDocument(),
    );
  });

  it("populates inputs from the profile and enables Save for an editor", async () => {
    mockApi({});
    mockPermissions({ canEdit: true });
    render(harness(newQc()));

    const display = (await screen.findByLabelText(/display name/i)) as HTMLInputElement;
    expect(display.value).toBe("Acme Corp");

    const desc = screen.getByLabelText(/description/i) as HTMLTextAreaElement;
    expect(desc.value).toBe("An engineering org");

    const tz = screen.getByLabelText(/default time zone/i) as HTMLSelectElement;
    expect(tz.value).toBe("Europe/Oslo");

    expect(screen.getByRole("button", { name: /save changes/i })).not.toBeDisabled();
  });

  it("read-only when the user lacks OrgProfileEdit", async () => {
    mockApi({});
    mockPermissions({ canEdit: false });
    render(harness(newQc()));

    const display = (await screen.findByLabelText(/display name/i)) as HTMLInputElement;
    await waitFor(() => expect(display).toBeDisabled());
    expect(screen.getByLabelText(/description/i)).toBeDisabled();
    expect(screen.getByLabelText(/default time zone/i)).toBeDisabled();
    expect(screen.getByRole("button", { name: /save changes/i })).toBeDisabled();
  });

  it("blocks submit on an empty displayName and surfaces the zod message", async () => {
    const put = vi.fn();
    mockApi({ put });
    mockPermissions({ canEdit: true });
    render(harness(newQc()));

    const display = (await screen.findByLabelText(/display name/i)) as HTMLInputElement;
    await userEvent.clear(display);
    await userEvent.click(screen.getByRole("button", { name: /save changes/i }));

    expect(await screen.findByText(/display name is required/i)).toBeInTheDocument();
    expect(put).not.toHaveBeenCalled();
  });

  it("happy submit: PUTs the new values (description='' → null) and toasts success", async () => {
    const put = vi.fn().mockResolvedValue({
      data: undefined,
      error: undefined,
      response: { status: 204 },
    });
    mockApi({ put });
    mockPermissions({ canEdit: true });
    render(harness(newQc()));

    const display = (await screen.findByLabelText(/display name/i)) as HTMLInputElement;
    await userEvent.clear(display);
    await userEvent.type(display, "Acme Renamed");

    const desc = screen.getByLabelText(/description/i) as HTMLTextAreaElement;
    await userEvent.clear(desc);

    await userEvent.click(screen.getByRole("button", { name: /save changes/i }));

    await waitFor(() => expect(put).toHaveBeenCalled());
    expect(put).toHaveBeenCalledWith(
      "/api/v1/organizations/me",
      expect.objectContaining({
        body: {
          displayName: "Acme Renamed",
          description: null,
          defaultTimeZone: "Europe/Oslo",
        },
      }),
    );
    await waitFor(() =>
      expect(screen.getByText(/organization profile saved/i)).toBeInTheDocument(),
    );
  });

  it("on 400 with a ProblemDetails errors map, applies server field errors", async () => {
    const put = vi.fn().mockResolvedValue({
      data: undefined,
      error: {
        type: "https://kartova.io/problems/validation",
        title: "Bad Request",
        errors: { displayName: ["Display name conflicts with another tenant."] },
      },
      response: { status: 400 },
    });
    mockApi({ put });
    mockPermissions({ canEdit: true });
    render(harness(newQc()));

    await screen.findByLabelText(/display name/i);
    await userEvent.click(screen.getByRole("button", { name: /save changes/i }));

    await waitFor(() => expect(put).toHaveBeenCalled());
    expect(
      await screen.findByText(/conflicts with another tenant/i),
    ).toBeInTheDocument();
  });

  it("on 412 ConcurrencyConflict, surfaces the reload toast", async () => {
    const put = vi.fn().mockResolvedValue({
      data: undefined,
      error: { type: "https://kartova.io/problems/concurrency-conflict", title: "stale" },
      response: { status: 412 },
    });
    mockApi({ put });
    mockPermissions({ canEdit: true });
    render(harness(newQc()));

    await screen.findByLabelText(/display name/i);
    await userEvent.click(screen.getByRole("button", { name: /save changes/i }));

    await waitFor(() =>
      expect(screen.getByText(/someone else edited the organization/i)).toBeInTheDocument(),
    );
  });

  it("on generic 500, toasts the problem detail", async () => {
    const put = vi.fn().mockResolvedValue({
      data: undefined,
      error: { detail: "Synthetic failure" },
      response: { status: 500 },
    });
    mockApi({ put });
    mockPermissions({ canEdit: true });
    render(harness(newQc()));

    await screen.findByLabelText(/display name/i);
    await userEvent.click(screen.getByRole("button", { name: /save changes/i }));

    await waitFor(() =>
      expect(screen.getByText(/synthetic failure/i)).toBeInTheDocument(),
    );
  });
});
