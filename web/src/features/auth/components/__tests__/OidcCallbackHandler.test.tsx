import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, waitFor } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";

import type { SessionStartResponse } from "@/features/auth/api/session";

// ---------- Mocks (hoisted) ----------

const navigateMock = vi.fn();
vi.mock("react-router-dom", async () => {
  const actual = await vi.importActual<typeof import("react-router-dom")>(
    "react-router-dom",
  );
  return {
    ...actual,
    useNavigate: () => navigateMock,
  };
});

const setQueryDataMock = vi.fn();
vi.mock("@tanstack/react-query", async () => {
  const actual = await vi.importActual<typeof import("@tanstack/react-query")>(
    "@tanstack/react-query",
  );
  return {
    ...actual,
    useQueryClient: () => ({ setQueryData: setQueryDataMock }),
  };
});

const mutateAsyncMock = vi.fn();
vi.mock("@/features/auth/api/session", () => ({
  useStartSession: () => ({ mutateAsync: mutateAsyncMock }),
}));

// Importing AFTER the mocks so the component picks them up.
import { OidcCallbackHandler } from "../OidcCallbackHandler";
import { orgKeys } from "@/features/organization/api/organization";

const ORG = {
  id: "o-1",
  displayName: "Acme Corp",
  description: null,
  defaultTimeZone: "UTC",
  logoEtag: null,
  logoMimeType: null,
  createdAt: "2026-01-01T00:00:00Z",
};

const RESPONSE_NO_INVITE: SessionStartResponse = {
  me: { id: "u-1", displayName: "Alice", email: "alice@example.com" },
  role: "Member",
  permissions: ["catalog.read"],
  teams: [],
  organization: ORG,
  acceptedInvitation: null,
};

const RESPONSE_WITH_INVITE: SessionStartResponse = {
  ...RESPONSE_NO_INVITE,
  acceptedInvitation: {
    orgDisplayName: "Acme Corp",
    invitedBy: { id: "u-2", displayName: "Bob", email: "bob@example.com" },
    invitedAt: "2026-01-01T00:00:00Z",
    acceptedAt: "2026-01-02T00:00:00Z",
  },
};

function renderHandler(returnTo?: string) {
  return render(
    <MemoryRouter>
      <OidcCallbackHandler returnTo={returnTo} />
    </MemoryRouter>,
  );
}

describe("OidcCallbackHandler", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("happy path: bootstraps session, populates org cache, navigates to /catalog", async () => {
    mutateAsyncMock.mockResolvedValue(RESPONSE_NO_INVITE);
    renderHandler();

    await waitFor(() => expect(mutateAsyncMock).toHaveBeenCalled());
    await waitFor(() =>
      expect(setQueryDataMock).toHaveBeenCalledWith(orgKeys.profile(), ORG),
    );
    await waitFor(() =>
      expect(navigateMock).toHaveBeenCalledWith("/catalog", { replace: true }),
    );
  });

  it("with returnTo: navigates back to the original deep link instead of /catalog", async () => {
    mutateAsyncMock.mockResolvedValue(RESPONSE_NO_INVITE);
    renderHandler("/catalog/services?displayNameContains=foo");

    await waitFor(() =>
      expect(navigateMock).toHaveBeenCalledWith(
        "/catalog/services?displayNameContains=foo",
        { replace: true },
      ),
    );
  });

  it("with both returnTo and acceptedInvitation: /welcome wins (invitation takes precedence)", async () => {
    mutateAsyncMock.mockResolvedValue(RESPONSE_WITH_INVITE);
    renderHandler("/teams/abc");

    await waitFor(() =>
      expect(navigateMock).toHaveBeenCalledWith("/welcome", {
        state: RESPONSE_WITH_INVITE.acceptedInvitation,
        replace: true,
      }),
    );
    expect(navigateMock).not.toHaveBeenCalledWith("/teams/abc", expect.anything());
  });

  it("with acceptedInvitation: navigates to /welcome with the invitation as router state", async () => {
    mutateAsyncMock.mockResolvedValue(RESPONSE_WITH_INVITE);
    renderHandler();

    await waitFor(() =>
      expect(navigateMock).toHaveBeenCalledWith("/welcome", {
        state: RESPONSE_WITH_INVITE.acceptedInvitation,
        replace: true,
      }),
    );
    // The org cache is still primed even when we route to /welcome.
    expect(setQueryDataMock).toHaveBeenCalledWith(orgKeys.profile(), ORG);
  });

  it("on session-bootstrap error: navigates to /login-error", async () => {
    mutateAsyncMock.mockRejectedValue(
      Object.assign(new Error("boom"), { __status: 502 }),
    );
    renderHandler();

    await waitFor(() =>
      expect(navigateMock).toHaveBeenCalledWith("/login-error", {
        replace: true,
      }),
    );
    expect(setQueryDataMock).not.toHaveBeenCalled();
  });

  it("renders the spinner while the bootstrap is in flight", () => {
    // Never-resolving promise so we can observe the in-flight UI.
    mutateAsyncMock.mockReturnValue(new Promise(() => {}));
    const { getByRole } = renderHandler();
    expect(getByRole("status")).toHaveAttribute("aria-label", "Signing you in…");
  });

  it("unmount before the bootstrap resolves does not touch navigate or query cache", async () => {
    let resolve!: (v: SessionStartResponse) => void;
    mutateAsyncMock.mockReturnValue(
      new Promise<SessionStartResponse>((r) => {
        resolve = r;
      }),
    );
    const { unmount } = renderHandler();

    unmount();
    resolve(RESPONSE_NO_INVITE);

    // Give microtasks a chance to drain.
    await new Promise((r) => setTimeout(r, 0));

    expect(setQueryDataMock).not.toHaveBeenCalled();
    expect(navigateMock).not.toHaveBeenCalled();
  });
});
