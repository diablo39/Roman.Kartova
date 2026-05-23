import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { Toaster } from "sonner";

import * as clientModule from "@/features/catalog/api/client";

import { ReactivateConfirmDialog } from "../ReactivateConfirmDialog";
import type { ApplicationResponse } from "@/features/catalog/api/applications";

const baseApp: ApplicationResponse = {
  id: "00000000-0000-0000-0000-000000000abc",
  tenantId: "t1",
  name: "payments",
  displayName: "Payments",
  description: "Payments service.",
  ownerUserId: "u1",
  createdAt: "2026-04-30T00:00:00Z",
  lifecycle: "deprecated",
  sunsetDate: "2026-01-01T00:00:00Z",
  version: "v1",
};

function setup({
  post,
  application = baseApp,
  open = true,
  onOpenChange = vi.fn(),
}: {
  post: ReturnType<typeof vi.fn>;
  application?: ApplicationResponse;
  open?: boolean;
  onOpenChange?: (b: boolean) => void;
}) {
  vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({
    GET: vi.fn(), POST: post,
  } as never);

  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={qc}>
      <Toaster />
      <ReactivateConfirmDialog application={application} open={open} onOpenChange={onOpenChange} />
    </QueryClientProvider>
  );
  return { onOpenChange };
}

describe("ReactivateConfirmDialog", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("renders the reactivate confirmation copy", () => {
    setup({ post: vi.fn() });
    expect(screen.getByText(/returns to/i)).toBeInTheDocument();
    expect(screen.getByText(/active/i)).toBeInTheDocument();
    expect(screen.getByText(/sunset date is cleared/i)).toBeInTheDocument();
  });

  it("calls POST /reactivate on confirm and closes on success", async () => {
    const successApp = { ...baseApp, lifecycle: "active" as const, sunsetDate: null };
    const post = vi.fn().mockResolvedValue({
      data: successApp,
      error: undefined,
      response: { status: 200 },
    });
    const onOpenChange = vi.fn();
    setup({ post, onOpenChange });

    await userEvent.click(screen.getByRole("button", { name: /^reactivate$/i }));

    await waitFor(() => expect(post).toHaveBeenCalled());
    const [url] = post.mock.calls[0] as [string, unknown];
    expect(url).toContain(`/reactivate`);
    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false));
  });

  it("Cancel closes the dialog without calling POST", async () => {
    const post = vi.fn();
    const onOpenChange = vi.fn();
    setup({ post, onOpenChange });

    await userEvent.click(screen.getByRole("button", { name: /cancel/i }));

    expect(post).not.toHaveBeenCalled();
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it("on 409 LifecycleConflict toasts the friendly current state label and closes", async () => {
    const problem = {
      type: "https://kartova.io/problems/lifecycle-conflict",
      title: "Lifecycle transition not allowed",
      currentLifecycle: "active",
      attemptedTransition: "Reactivate",
    };
    const post = vi.fn().mockResolvedValue({
      data: undefined,
      error: problem,
      response: { status: 409 },
    });
    const onOpenChange = vi.fn();
    setup({ post, onOpenChange });

    await userEvent.click(screen.getByRole("button", { name: /^reactivate$/i }));

    await waitFor(() =>
      expect(screen.getByText(/current state is Active/)).toBeInTheDocument()
    );
    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false));
  });

  it("falls back to a toast when the error is unrecognised and dialog stays open", async () => {
    const problem = { status: 500, title: "Bang", detail: "Server exploded." };
    const post = vi.fn().mockResolvedValue({
      data: undefined,
      error: problem,
      response: { status: 500 },
    });
    const onOpenChange = vi.fn();
    setup({ post, onOpenChange });

    await userEvent.click(screen.getByRole("button", { name: /^reactivate$/i }));

    await waitFor(() => expect(screen.getByText(/server exploded/i)).toBeInTheDocument());
    expect(onOpenChange).not.toHaveBeenCalledWith(false);
  });
});
