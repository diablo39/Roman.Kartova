import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import {
  getInvitationAcceptContext,
  acceptInvitation,
} from "../acceptInvitation";

function makeFetchSpy(
  status: number,
  body: unknown,
): { spy: ReturnType<typeof vi.fn>; lastRequest: () => Request | undefined } {
  let lastReq: Request | undefined;
  const spy = vi.fn(async (input: Request | string | URL, _init?: RequestInit) => {
    lastReq = input instanceof Request ? input : undefined;
    return new Response(JSON.stringify(body), {
      status,
      headers: { "Content-Type": "application/json" },
    });
  });
  return { spy, lastRequest: () => lastReq };
}

describe("acceptInvitation API module", () => {
  let originalFetch: typeof globalThis.fetch;

  beforeEach(() => {
    originalFetch = globalThis.fetch;
  });

  afterEach(() => {
    globalThis.fetch = originalFetch;
    vi.restoreAllMocks();
  });

  describe("getInvitationAcceptContext", () => {
    it("issues a GET to /api/v1/invitations/accept with token query param", async () => {
      const ctx = {
        orgDisplayName: "Acme",
        invitedBy: { userId: "u1", displayName: "Alice", email: "alice@acme.com" },
        invitedAt: "2026-01-01T00:00:00Z",
        acceptedAt: null,
      };
      const { spy, lastRequest } = makeFetchSpy(200, ctx);
      globalThis.fetch = spy as unknown as typeof fetch;

      const result = await getInvitationAcceptContext("TOK");

      expect(spy).toHaveBeenCalledOnce();
      const req = lastRequest()!;
      expect(req.url).toContain("/api/v1/invitations/accept");
      expect(req.url).toContain("token=TOK");
      expect(result).toEqual(ctx);
    });

    it("does NOT send an Authorization header", async () => {
      const ctx = {
        orgDisplayName: "Acme",
        invitedBy: { userId: "u1", displayName: "Alice", email: "alice@acme.com" },
        invitedAt: "2026-01-01T00:00:00Z",
        acceptedAt: null,
      };
      const { spy, lastRequest } = makeFetchSpy(200, ctx);
      globalThis.fetch = spy as unknown as typeof fetch;

      await getInvitationAcceptContext("TOK");

      const req = lastRequest()!;
      expect(req.headers.has("Authorization")).toBe(false);
    });

    it("throws with __status on a 410 Gone response", async () => {
      const { spy } = makeFetchSpy(410, {
        title: "Gone",
        detail: "Invitation expired",
      });
      globalThis.fetch = spy as unknown as typeof fetch;

      await expect(getInvitationAcceptContext("EXPIRED")).rejects.toMatchObject({
        __status: 410,
      });
    });

    it("throws with __status on a 404 Not Found response", async () => {
      const { spy } = makeFetchSpy(404, { title: "Not Found" });
      globalThis.fetch = spy as unknown as typeof fetch;

      await expect(getInvitationAcceptContext("MISSING")).rejects.toMatchObject({
        __status: 404,
      });
    });
  });

  describe("acceptInvitation", () => {
    it("POSTs the token/password/displayName body and returns email on 200", async () => {
      const { spy } = makeFetchSpy(200, { email: "bob@acme.com" });
      globalThis.fetch = spy as unknown as typeof fetch;

      const result = await acceptInvitation({
        token: "TOK",
        password: "S3cr3t!",
        displayName: "Bob",
      });

      expect(spy).toHaveBeenCalledOnce();
      expect(result).toEqual({ email: "bob@acme.com" });
    });

    it("sends the body as JSON", async () => {
      let capturedBody: unknown;
      const spy = vi.fn(async (input: Request | string | URL, _init?: RequestInit) => {
        const req = input instanceof Request ? input : new Request(input as string);
        capturedBody = await req.json();
        return new Response(JSON.stringify({ email: "bob@acme.com" }), {
          status: 200,
          headers: { "Content-Type": "application/json" },
        });
      });
      globalThis.fetch = spy as unknown as typeof fetch;

      await acceptInvitation({
        token: "TOK",
        password: "S3cr3t!",
        displayName: "Bob",
      });

      expect(capturedBody).toEqual({
        token: "TOK",
        password: "S3cr3t!",
        displayName: "Bob",
      });
    });

    it("does NOT send an Authorization header", async () => {
      let lastReq: Request | undefined;
      const spy = vi.fn(async (input: Request | string | URL, _init?: RequestInit) => {
        lastReq = input instanceof Request ? input : undefined;
        return new Response(JSON.stringify({ email: "bob@acme.com" }), {
          status: 200,
          headers: { "Content-Type": "application/json" },
        });
      });
      globalThis.fetch = spy as unknown as typeof fetch;

      await acceptInvitation({ token: "T", password: "p", displayName: "D" });

      expect(lastReq?.headers.has("Authorization")).toBe(false);
    });

    it("throws with __status 410 on a Gone response", async () => {
      const { spy } = makeFetchSpy(410, { title: "Gone" });
      globalThis.fetch = spy as unknown as typeof fetch;

      await expect(
        acceptInvitation({ token: "EXPIRED", password: "x", displayName: "D" }),
      ).rejects.toMatchObject({ __status: 410 });
    });

    it("throws with __status 400 on a Bad Request response", async () => {
      const { spy } = makeFetchSpy(400, {
        title: "Bad Request",
        detail: "Weak password",
      });
      globalThis.fetch = spy as unknown as typeof fetch;

      await expect(
        acceptInvitation({ token: "T", password: "weak", displayName: "D" }),
      ).rejects.toMatchObject({ __status: 400 });
    });

    it("throws with __status 502 on a Bad Gateway response", async () => {
      const { spy } = makeFetchSpy(502, { title: "Bad Gateway" });
      globalThis.fetch = spy as unknown as typeof fetch;

      await expect(
        acceptInvitation({ token: "T", password: "S3cr3t!", displayName: "D" }),
      ).rejects.toMatchObject({ __status: 502 });
    });
  });
});
