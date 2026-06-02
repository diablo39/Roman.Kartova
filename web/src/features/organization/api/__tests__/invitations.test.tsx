import React from "react";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";

import * as clientModule from "@/features/catalog/api/client";
import {
  invitationKeys,
  useInvitationsList,
  useCreateInvitation,
  useRevokeInvitation,
} from "../invitations";

function makeWrapper(qc: QueryClient) {
  return ({ children }: { children: React.ReactNode }) => (
    <QueryClientProvider client={qc}>{children}</QueryClientProvider>
  );
}

function newQc(): QueryClient {
  return new QueryClient({
    defaultOptions: { queries: { retry: false, gcTime: 0 } },
  });
}

function mockApiClient(impl: {
  GET?: ReturnType<typeof vi.fn>;
  POST?: ReturnType<typeof vi.fn>;
  PUT?: ReturnType<typeof vi.fn>;
  DELETE?: ReturnType<typeof vi.fn>;
}) {
  vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
    GET: impl.GET ?? vi.fn(),
    POST: impl.POST ?? vi.fn(),
    PUT: impl.PUT ?? vi.fn(),
    DELETE: impl.DELETE ?? vi.fn(),
  } as never);
}

const DEFAULT_LIST_PARAMS = {
  sortBy: "invitedAt" as const,
  sortOrder: "desc" as const,
};

const INVITATION = {
  id: "00000000-0000-0000-0000-000000000001",
  email: "alice@example.com",
  role: "Member",
  invitedAt: "2026-01-01T00:00:00Z",
  expiresAt: "2026-01-08T00:00:00Z",
  status: "Pending",
  invitedByUserId: "00000000-0000-0000-0000-0000000000aa",
  acceptedAt: null,
  revokedAt: null,
};

describe("invitations hooks", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  describe("useInvitationsList", () => {
    it("calls GET /api/v1/organizations/invitations with sort params", async () => {
      const page = { items: [INVITATION], nextCursor: null, prevCursor: null };
      const get = vi.fn().mockResolvedValue({ data: page, error: undefined });
      mockApiClient({ GET: get });

      const { result } = renderHook(
        () => useInvitationsList(DEFAULT_LIST_PARAMS),
        { wrapper: makeWrapper(newQc()) },
      );

      await waitFor(() => expect(result.current.isLoading).toBe(false));
      expect(get).toHaveBeenCalledWith(
        "/api/v1/organizations/invitations",
        expect.objectContaining({
          params: expect.objectContaining({
            query: expect.objectContaining({
              sortBy: "invitedAt",
              sortOrder: "desc",
            }),
          }),
        }),
      );
      expect(result.current.items).toHaveLength(1);
    });

    it("surfaces API errors as query error state", async () => {
      const get = vi.fn().mockResolvedValue({
        data: undefined,
        error: { status: 500, title: "boom" },
      });
      mockApiClient({ GET: get });

      const { result } = renderHook(
        () => useInvitationsList(DEFAULT_LIST_PARAMS),
        { wrapper: makeWrapper(newQc()) },
      );

      await waitFor(() => expect(result.current.isError).toBe(true));
    });

    it("passes the status filter through when supplied", async () => {
      const get = vi.fn().mockResolvedValue({
        data: { items: [], nextCursor: null, prevCursor: null },
        error: undefined,
      });
      mockApiClient({ GET: get });

      renderHook(
        () => useInvitationsList({ ...DEFAULT_LIST_PARAMS, status: "Pending" }),
        { wrapper: makeWrapper(newQc()) },
      );

      await waitFor(() => expect(get).toHaveBeenCalled());
      expect(get).toHaveBeenCalledWith(
        "/api/v1/organizations/invitations",
        expect.objectContaining({
          params: expect.objectContaining({
            query: expect.objectContaining({ status: "Pending" }),
          }),
        }),
      );
    });
  });

  describe("useCreateInvitation", () => {
    it("posts the body and exposes the response on success", async () => {
      const responseBody = { invitation: INVITATION, inviteUrl: "https://example/i/x" };
      const post = vi.fn().mockResolvedValue({
        data: responseBody,
        error: undefined,
        response: { status: 201 },
      });
      mockApiClient({ POST: post });

      const { result } = renderHook(() => useCreateInvitation(), {
        wrapper: makeWrapper(newQc()),
      });

      const out = await result.current.mutateAsync({
        email: "alice@example.com",
        role: "Member",
      });
      expect(out).toEqual(responseBody);
      expect(post).toHaveBeenCalledWith(
        "/api/v1/organizations/invitations",
        { body: { email: "alice@example.com", role: "Member" } },
      );
    });

    it("invalidates the invitations list on success", async () => {
      const post = vi.fn().mockResolvedValue({
        data: { invitation: INVITATION, inviteUrl: "x" },
        error: undefined,
        response: { status: 201 },
      });
      mockApiClient({ POST: post });

      const qc = newQc();
      const invalidate = vi.spyOn(qc, "invalidateQueries");

      const { result } = renderHook(() => useCreateInvitation(), {
        wrapper: makeWrapper(qc),
      });
      await result.current.mutateAsync({
        email: "alice@example.com",
        role: "Member",
      });

      expect(invalidate).toHaveBeenCalledWith({ queryKey: invitationKeys.all });
    });

    it("on 409 attaches __status and preserves the problem 'type' on the thrown error", async () => {
      const errorBody = {
        type: "https://kartova.io/problems/email-already-in-tenant",
        title: "Conflict",
        detail: "Already a member.",
      };
      const post = vi.fn().mockResolvedValue({
        data: undefined,
        error: errorBody,
        response: { status: 409 },
      });
      mockApiClient({ POST: post });

      const { result } = renderHook(() => useCreateInvitation(), {
        wrapper: makeWrapper(newQc()),
      });
      await expect(
        result.current.mutateAsync({ email: "a@x", role: "Member" }),
      ).rejects.toMatchObject({
        __status: 409,
        type: "https://kartova.io/problems/email-already-in-tenant",
      });
    });

    it("on 422 attaches __status to the thrown error", async () => {
      const post = vi.fn().mockResolvedValue({
        data: undefined,
        error: { title: "Unprocessable", detail: "bad email" },
        response: { status: 422 },
      });
      mockApiClient({ POST: post });

      const { result } = renderHook(() => useCreateInvitation(), {
        wrapper: makeWrapper(newQc()),
      });
      await expect(
        result.current.mutateAsync({ email: "a@x", role: "Member" }),
      ).rejects.toMatchObject({ __status: 422 });
    });
  });

  describe("useRevokeInvitation", () => {
    it("posts to the revoke endpoint with the path id and invalidates the list on success", async () => {
      const post = vi.fn().mockResolvedValue({
        data: undefined,
        error: undefined,
        response: { status: 204 },
      });
      mockApiClient({ POST: post });

      const qc = newQc();
      const invalidate = vi.spyOn(qc, "invalidateQueries");

      const { result } = renderHook(() => useRevokeInvitation("inv-1"), {
        wrapper: makeWrapper(qc),
      });
      await result.current.mutateAsync();

      expect(post).toHaveBeenCalledWith(
        "/api/v1/organizations/invitations/{id}/revoke",
        { params: { path: { id: "inv-1" } } },
      );
      expect(invalidate).toHaveBeenCalledWith({ queryKey: invitationKeys.all });
    });

    it("on 409 attaches __status to the thrown error", async () => {
      const post = vi.fn().mockResolvedValue({
        data: undefined,
        error: { title: "Conflict" },
        response: { status: 409 },
      });
      mockApiClient({ POST: post });

      const { result } = renderHook(() => useRevokeInvitation("inv-9"), {
        wrapper: makeWrapper(newQc()),
      });
      await expect(result.current.mutateAsync()).rejects.toMatchObject({
        __status: 409,
      });
    });
  });
});
