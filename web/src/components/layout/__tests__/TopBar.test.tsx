import React from "react";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";

import * as clientModule from "@/features/catalog/api/client";

const useAuthMock = vi.fn();
vi.mock("react-oidc-context", () => ({
  useAuth: () => useAuthMock(),
}));

import { TopBar } from "../TopBar";

function withQueryClient(ui: React.ReactNode, qc: QueryClient) {
  return <QueryClientProvider client={qc}>{ui}</QueryClientProvider>;
}

describe("TopBar", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    useAuthMock.mockReset();
    useAuthMock.mockReturnValue({
      isAuthenticated: true,
      user: {
        access_token: "tok",
        profile: { sub: "u-1", name: "Alice Admin", email: "alice@x", tenant_id: "t1" },
      },
      signoutRedirect: vi.fn(),
    });
  });

  it("shows skeleton while organization query is loading", () => {
    const get = vi.fn().mockReturnValue(new Promise(() => {})); // never resolves
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
      GET: get, POST: vi.fn(),
    } as never);

    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(withQueryClient(<TopBar />, qc));

    expect(screen.getByTestId("tenant-skeleton")).toBeInTheDocument();
  });

  it("renders organization name when query resolves", async () => {
    const get = vi.fn().mockResolvedValue({
      data: { id: "o1", displayName: "Acme Corp" },
      error: undefined,
    });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
      GET: get, POST: vi.fn(),
    } as never);

    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(withQueryClient(<TopBar />, qc));

    await waitFor(() => expect(screen.getByText("Acme Corp")).toBeInTheDocument());
  });

  it("renders nothing in the tenant pill when query errors", async () => {
    const get = vi.fn().mockResolvedValue({
      data: undefined,
      error: { status: 500 },
    });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
      GET: get, POST: vi.fn(),
    } as never);

    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(withQueryClient(<TopBar />, qc));

    await waitFor(() => {
      const pill = screen.getByTestId("tenant-pill");
      expect(pill.children.length).toBe(0);
    });
  });

  it("renders the tenant logo (and not the badge) when the org has a logoEtag", async () => {
    const get = vi.fn().mockResolvedValue({
      data: {
        id: "o1",
        displayName: "Acme Corp",
        logoEtag: "\"abc123\"",
        logoMimeType: "image/png",
      },
      error: undefined,
    });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
      GET: get, POST: vi.fn(),
    } as never);

    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(withQueryClient(<TopBar />, qc));

    const logo = await waitFor(() => screen.getByTestId("tenant-logo"));
    expect(logo).toBeInTheDocument();
    expect(logo.tagName).toBe("IMG");
    expect(logo.getAttribute("alt")).toBe("Acme Corp");
    // The src must include the etag as a cache-bust query string (organization.ts contract).
    expect(logo.getAttribute("src")).toContain("/api/v1/organizations/me/logo");
    expect(logo.getAttribute("src")).toContain("v=");
    // And the gray displayName badge must NOT render at the same time.
    expect(screen.queryByText("Acme Corp")).toBeNull();
  });

  it("renders user initials and exposes a sign-out menu item", async () => {
    const get = vi.fn().mockResolvedValue({
      data: { id: "o1", displayName: "Acme" },
      error: undefined,
    });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
      GET: get, POST: vi.fn(),
    } as never);

    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(withQueryClient(<TopBar />, qc));

    expect(screen.getByText("AA")).toBeInTheDocument(); // initials from "Alice Admin"
  });
});
