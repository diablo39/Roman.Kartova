import React from "react";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { renderHook } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";

import * as clientModule from "@/features/catalog/api/client";

import { useStartSession, type SessionStartResponse } from "../session";

function makeWrapper(qc: QueryClient) {
  return ({ children }: { children: React.ReactNode }) => (
    <QueryClientProvider client={qc}>{children}</QueryClientProvider>
  );
}

function newQueryClient(): QueryClient {
  return new QueryClient({
    defaultOptions: { mutations: { retry: false } },
  });
}

function mockApiClient(post: ReturnType<typeof vi.fn>) {
  vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
    GET: vi.fn(),
    POST: post,
    PUT: vi.fn(),
    DELETE: vi.fn(),
  } as never);
}

const RESPONSE: SessionStartResponse = {
  me: { id: "u-1", displayName: "Alice", email: "alice@example.com" },
  role: "Member",
  permissions: ["catalog.read"],
  teams: [{ teamId: "t-1", role: "Member" }],
  organization: {
    id: "o-1",
    displayName: "Acme Corp",
    description: null,
    defaultTimeZone: "UTC",
    logoEtag: null,
    logoMimeType: null,
    createdAt: "2026-01-01T00:00:00Z",
  },
  acceptedInvitation: null,
};

describe("useStartSession", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it("POSTs /api/v1/auth/session and returns the session response", async () => {
    const post = vi.fn().mockResolvedValue({
      data: RESPONSE,
      error: undefined,
      response: { status: 200 } as Response,
    });
    mockApiClient(post);

    const { result } = renderHook(() => useStartSession(), {
      wrapper: makeWrapper(newQueryClient()),
    });

    const data = await result.current.mutateAsync();
    expect(post).toHaveBeenCalledWith("/api/v1/auth/session", {});
    expect(data).toEqual(RESPONSE);
  });

  it("attaches __status: 401 on unauthorized response", async () => {
    const post = vi.fn().mockResolvedValue({
      data: undefined,
      error: { title: "Unauthorized" },
      response: { status: 401 } as Response,
    });
    mockApiClient(post);

    const { result } = renderHook(() => useStartSession(), {
      wrapper: makeWrapper(newQueryClient()),
    });
    await expect(result.current.mutateAsync()).rejects.toMatchObject({
      __status: 401,
    });
  });

  it("attaches __status: 502 on upstream failure", async () => {
    const post = vi.fn().mockResolvedValue({
      data: undefined,
      error: { title: "Bad Gateway" },
      response: { status: 502 } as Response,
    });
    mockApiClient(post);

    const { result } = renderHook(() => useStartSession(), {
      wrapper: makeWrapper(newQueryClient()),
    });
    await expect(result.current.mutateAsync()).rejects.toMatchObject({
      __status: 502,
    });
  });

  it("passes through acceptedInvitation when the server attached one", async () => {
    const withInvite: SessionStartResponse = {
      ...RESPONSE,
      acceptedInvitation: {
        orgDisplayName: "Acme Corp",
        invitedBy: { id: "u-2", displayName: "Bob", email: "bob@example.com" },
        invitedAt: "2026-01-01T00:00:00Z",
        acceptedAt: "2026-01-02T00:00:00Z",
      },
    };
    const post = vi.fn().mockResolvedValue({
      data: withInvite,
      error: undefined,
      response: { status: 200 } as Response,
    });
    mockApiClient(post);

    const { result } = renderHook(() => useStartSession(), {
      wrapper: makeWrapper(newQueryClient()),
    });
    const data = await result.current.mutateAsync();
    expect(data.acceptedInvitation).not.toBeNull();
    expect(data.acceptedInvitation?.orgDisplayName).toBe("Acme Corp");
  });
});
