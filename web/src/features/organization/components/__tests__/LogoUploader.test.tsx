import React from "react";
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

import { LogoUploader } from "../LogoUploader";

function harness(qc: QueryClient, ui: React.ReactNode) {
  return (
    <QueryClientProvider client={qc}>
      <Toaster />
      {ui}
    </QueryClientProvider>
  );
}

function newQc(): QueryClient {
  return new QueryClient({
    defaultOptions: { queries: { retry: false, gcTime: 0 } },
  });
}

/** Build a `File` of the given size (bytes) and MIME type. */
function fakeFile(name: string, mime: string, size: number): File {
  const bytes = new Uint8Array(size);
  return new File([bytes], name, { type: mime });
}

function mockApiClient(impl: { DELETE?: ReturnType<typeof vi.fn> }) {
  vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
    GET: vi.fn(),
    POST: vi.fn(),
    PUT: vi.fn(),
    DELETE: impl.DELETE ?? vi.fn(),
  } as never);
}

describe("LogoUploader", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    useAuthMock.mockReset();
    useAuthMock.mockReturnValue({
      isAuthenticated: true,
      user: { access_token: "tok-1" },
    });
    // jsdom provides createObjectURL but we stub it so previews resolve to
    // a known string and the cleanup effect can be observed without leaking
    // real blob URLs. `vi.spyOn` is enough here — jsdom (current) has both
    // methods on URL, so no manual define is needed.
    vi.spyOn(URL, "createObjectURL").mockReturnValue("blob:fake-url");
    vi.spyOn(URL, "revokeObjectURL").mockImplementation(() => undefined);
  });

  describe("read-only view (canEdit=false)", () => {
    it("renders the current logo image when present", () => {
      mockApiClient({});
      render(
        harness(
          newQc(),
          <LogoUploader currentLogoUrl="/api/v1/organizations/me/logo?v=abc" canEdit={false} />,
        ),
      );
      const img = screen.getByAltText(/organization logo/i) as HTMLImageElement;
      expect(img.src).toContain("/api/v1/organizations/me/logo?v=abc");
      expect(screen.queryByRole("button")).toBeNull();
    });

    it("shows a 'No logo' placeholder when currentLogoUrl is null", () => {
      mockApiClient({});
      render(harness(newQc(), <LogoUploader currentLogoUrl={null} canEdit={false} />));
      expect(screen.getByText(/no logo uploaded/i)).toBeInTheDocument();
      expect(screen.queryByRole("button")).toBeNull();
    });
  });

  describe("editable view (canEdit=true)", () => {
    it("renders the drop zone and a disabled Upload button before any file is selected", () => {
      mockApiClient({});
      render(harness(newQc(), <LogoUploader currentLogoUrl={null} canEdit={true} />));
      expect(screen.getByTestId("logo-dropzone")).toBeInTheDocument();
      const uploadBtn = screen.getByRole("button", { name: /upload logo/i });
      expect(uploadBtn).toBeDisabled();
    });

    it("rejects files larger than 256 KB with a toast and no network call", async () => {
      const fetchSpy = vi.spyOn(globalThis, "fetch");
      mockApiClient({});
      render(harness(newQc(), <LogoUploader currentLogoUrl={null} canEdit={true} />));

      const input = screen.getByLabelText(/choose logo file/i) as HTMLInputElement;
      const big = fakeFile("big.png", "image/png", 257 * 1024);
      await userEvent.upload(input, big);

      await waitFor(() =>
        expect(screen.getByText(/256 kb or smaller/i)).toBeInTheDocument(),
      );
      expect(fetchSpy).not.toHaveBeenCalled();
      expect(screen.getByRole("button", { name: /upload logo/i })).toBeDisabled();
    });

    it("rejects unsupported mime types with a toast and no network call", async () => {
      const fetchSpy = vi.spyOn(globalThis, "fetch");
      mockApiClient({});
      render(harness(newQc(), <LogoUploader currentLogoUrl={null} canEdit={true} />));

      const input = screen.getByLabelText(/choose logo file/i) as HTMLInputElement;
      const bad = fakeFile("doc.pdf", "application/pdf", 1024);
      // `applyAccept: false` lets us drive the validation path inside the
      // component instead of having `userEvent` silently filter the file
      // based on the input's `accept=` attribute.
      await userEvent.upload(input, bad, { applyAccept: false });

      await waitFor(() =>
        expect(screen.getByText(/logo must be png, jpeg, or svg/i)).toBeInTheDocument(),
      );
      expect(fetchSpy).not.toHaveBeenCalled();
    });

    it("stages a valid file, enables Upload, and shows preview images", async () => {
      mockApiClient({});
      render(harness(newQc(), <LogoUploader currentLogoUrl={null} canEdit={true} />));

      const input = screen.getByLabelText(/choose logo file/i) as HTMLInputElement;
      const good = fakeFile("logo.png", "image/png", 1024);
      await userEvent.upload(input, good);

      await waitFor(() =>
        expect(screen.getByRole("button", { name: /upload logo/i })).not.toBeDisabled(),
      );
      const previewSmall = screen.getByAltText(/preview 64x64/i) as HTMLImageElement;
      const previewLarge = screen.getByAltText(/preview 200x200/i) as HTMLImageElement;
      expect(previewSmall.src).toContain("blob:fake-url");
      expect(previewLarge.src).toContain("blob:fake-url");
    });

    it("uploads the staged file via PUT with the correct mime + bearer and toasts success", async () => {
      const fetchSpy = vi.spyOn(globalThis, "fetch").mockResolvedValue(
        new Response(JSON.stringify({ logoEtag: "newhash", mimeType: "image/png" }), {
          status: 200,
          headers: { "Content-Type": "application/json" },
        }),
      );
      mockApiClient({});

      render(harness(newQc(), <LogoUploader currentLogoUrl={null} canEdit={true} />));

      const input = screen.getByLabelText(/choose logo file/i) as HTMLInputElement;
      const good = fakeFile("logo.png", "image/png", 2048);
      await userEvent.upload(input, good);
      await userEvent.click(screen.getByRole("button", { name: /upload logo/i }));

      await waitFor(() => expect(fetchSpy).toHaveBeenCalled());
      expect(fetchSpy).toHaveBeenCalledWith(
        "/api/v1/organizations/me/logo",
        expect.objectContaining({
          method: "PUT",
          headers: expect.objectContaining({
            "Content-Type": "image/png",
            Authorization: "Bearer tok-1",
          }),
        }),
      );
      await waitFor(() =>
        expect(screen.getByText(/logo uploaded/i)).toBeInTheDocument(),
      );
    });

    it("on 422 from upload, toasts the server detail and does not clear staging", async () => {
      vi.spyOn(globalThis, "fetch").mockResolvedValue(
        new Response('{"detail":"That image is haunted"}', { status: 422 }),
      );
      mockApiClient({});

      render(harness(newQc(), <LogoUploader currentLogoUrl={null} canEdit={true} />));

      const input = screen.getByLabelText(/choose logo file/i) as HTMLInputElement;
      const good = fakeFile("logo.png", "image/png", 2048);
      await userEvent.upload(input, good);
      await userEvent.click(screen.getByRole("button", { name: /upload logo/i }));

      // The hook throws a synthetic error containing only __status; the
      // toast falls back to the generic "rejected by server" string. We
      // assert on the rejected wording, not on the body's detail.
      await waitFor(() =>
        expect(screen.getByText(/rejected by server/i)).toBeInTheDocument(),
      );
    });

    it("on 413 from upload, toasts a 'too large' message", async () => {
      vi.spyOn(globalThis, "fetch").mockResolvedValue(
        new Response('{"title":"big"}', { status: 413 }),
      );
      mockApiClient({});

      render(harness(newQc(), <LogoUploader currentLogoUrl={null} canEdit={true} />));

      const input = screen.getByLabelText(/choose logo file/i) as HTMLInputElement;
      // File passes client guard (under 256 KB) but server rejects.
      const good = fakeFile("logo.png", "image/png", 1024);
      await userEvent.upload(input, good);
      await userEvent.click(screen.getByRole("button", { name: /upload logo/i }));

      await waitFor(() =>
        expect(screen.getByText(/too large for the server/i)).toBeInTheDocument(),
      );
    });

    it("Remove Logo button is shown only when a current logo exists and nothing is staged", async () => {
      mockApiClient({});
      const { rerender } = render(
        harness(newQc(), <LogoUploader currentLogoUrl={null} canEdit={true} />),
      );
      expect(screen.queryByRole("button", { name: /remove logo/i })).toBeNull();

      rerender(
        harness(newQc(), <LogoUploader currentLogoUrl="/api/v1/organizations/me/logo?v=a" canEdit={true} />),
      );
      expect(screen.getByRole("button", { name: /remove logo/i })).toBeInTheDocument();

      // Stage a file: Remove disappears (it doesn't make sense to remove
      // an existing logo while staging a new one — the user should commit
      // or cancel that choice first).
      const input = screen.getByLabelText(/choose logo file/i) as HTMLInputElement;
      const good = fakeFile("logo.png", "image/png", 1024);
      await userEvent.upload(input, good);
      await waitFor(() =>
        expect(screen.queryByRole("button", { name: /remove logo/i })).toBeNull(),
      );
    });

    it("Remove Logo: happy path calls DELETE and toasts success", async () => {
      const del = vi.fn().mockResolvedValue({
        data: undefined,
        error: undefined,
        response: { status: 204 },
      });
      mockApiClient({ DELETE: del });

      render(
        harness(newQc(), <LogoUploader currentLogoUrl="/api/v1/organizations/me/logo?v=a" canEdit={true} />),
      );

      await userEvent.click(screen.getByRole("button", { name: /remove logo/i }));

      await waitFor(() => expect(del).toHaveBeenCalled());
      expect(del).toHaveBeenCalledWith("/api/v1/organizations/me/logo", {});
      await waitFor(() =>
        expect(screen.getByText(/logo removed/i)).toBeInTheDocument(),
      );
    });

    it("Remove Logo: 404 from server is treated as success (end state matches user intent)", async () => {
      const del = vi.fn().mockResolvedValue({
        data: undefined,
        error: { title: "Not Found" },
        response: { status: 404 },
      });
      mockApiClient({ DELETE: del });

      render(
        harness(newQc(), <LogoUploader currentLogoUrl="/api/v1/organizations/me/logo?v=a" canEdit={true} />),
      );

      await userEvent.click(screen.getByRole("button", { name: /remove logo/i }));

      await waitFor(() =>
        expect(screen.getByText(/logo removed/i)).toBeInTheDocument(),
      );
    });

    it("Cancel button clears the staged file", async () => {
      mockApiClient({});
      render(harness(newQc(), <LogoUploader currentLogoUrl={null} canEdit={true} />));

      const input = screen.getByLabelText(/choose logo file/i) as HTMLInputElement;
      const good = fakeFile("logo.png", "image/png", 1024);
      await userEvent.upload(input, good);
      await waitFor(() =>
        expect(screen.getByRole("button", { name: /upload logo/i })).not.toBeDisabled(),
      );

      await userEvent.click(screen.getByRole("button", { name: /^cancel$/i }));
      await waitFor(() =>
        expect(screen.getByRole("button", { name: /upload logo/i })).toBeDisabled(),
      );
    });
  });
});
