import { it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";

vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));
import { toast } from "sonner";

import { AddSystemMemberDialog } from "@/features/catalog/components/AddSystemMemberDialog";
import * as systems from "@/features/catalog/api/systems";
import * as rel from "@/features/catalog/api/relationships";
import * as clientModule from "@/features/catalog/api/client";

const mutateAsync = vi.fn().mockResolvedValue({ systemId: "sys1", systemDisplayName: "Payments" });

beforeEach(() => {
  vi.restoreAllMocks();
  vi.clearAllMocks();
  mutateAsync.mockReset().mockResolvedValue({ systemId: "sys1", systemDisplayName: "Payments" });
  vi.spyOn(systems, "useSetComponentSystem").mockReturnValue({ mutateAsync, isPending: false } as never);
});

it("assigns the selected Application to this System", async () => {
  vi.spyOn(rel, "useEntitySearch").mockReturnValue({
    data: [{ kind: "application", id: "a1", displayName: "Checkout" }], isLoading: false,
  } as never);
  render(<AddSystemMemberDialog open onOpenChange={vi.fn()} system={{ id: "sys1", displayName: "Payments" }} />);

  fireEvent.change(screen.getByRole("combobox"), { target: { value: "Che" } });
  await waitFor(() => expect(screen.getByText("Checkout")).toBeInTheDocument());
  fireEvent.click(screen.getByText("Checkout"));

  await waitFor(() =>
    expect(mutateAsync).toHaveBeenCalledWith({ componentKind: "application", componentId: "a1", systemId: "sys1" }),
  );
});

it("searches services when the kind is switched to Service", async () => {
  const search = vi.spyOn(rel, "useEntitySearch").mockReturnValue({ data: [], isLoading: false } as never);
  render(<AddSystemMemberDialog open onOpenChange={vi.fn()} system={{ id: "sys1", displayName: "Payments" }} />);

  fireEvent.click(screen.getByRole("radio", { name: /service/i }));

  await waitFor(() => expect(search).toHaveBeenLastCalledWith("service", expect.anything(), expect.anything()));
});

it("toasts the ProblemDetails message and keeps the dialog open when assign fails", async () => {
  mutateAsync.mockRejectedValueOnce({
    title: "Conflict",
    detail: "That System is archived and cannot accept new members.",
  });
  vi.spyOn(rel, "useEntitySearch").mockReturnValue({
    data: [{ kind: "application", id: "a1", displayName: "Checkout" }], isLoading: false,
  } as never);
  const onOpenChange = vi.fn();
  render(<AddSystemMemberDialog open onOpenChange={onOpenChange} system={{ id: "sys1", displayName: "Payments" }} />);

  fireEvent.change(screen.getByRole("combobox"), { target: { value: "Che" } });
  await waitFor(() => expect(screen.getByText("Checkout")).toBeInTheDocument());
  fireEvent.click(screen.getByText("Checkout"));

  await waitFor(() =>
    expect(toast.error).toHaveBeenCalledWith("That System is archived and cannot accept new members."),
  );
  expect(onOpenChange).not.toHaveBeenCalledWith(false);
});

it("toasts the 403-specific message for a genuine empty-body Forbid response", async () => {
  // Regression test for the gate-8 finding: SetComponentSystemAsync returns Results.Forbid()
  // on a 403 — an ASP.NET ForbidResult with NO response body. openapi-fetch resolves that as
  // { data: undefined, error: undefined, response } (Content-Length: 0, !response.ok), which is
  // exactly the shape the other tests in this file CANNOT reproduce: they reject `mutateAsync`
  // with a hand-built object that already carries `__status`, bypassing the real
  // unwrapData/throwWithStatus code path entirely — which is why the bug shipped. Mock the API
  // CLIENT instead so the real `useSetComponentSystem` mutation (and the real
  // unwrapData/throwWithStatus helpers) run for real.
  vi.spyOn(systems, "useSetComponentSystem").mockRestore();
  vi.spyOn(rel, "useEntitySearch").mockReturnValue({
    data: [{ kind: "application", id: "a1", displayName: "Checkout" }], isLoading: false,
  } as never);
  const put = vi.fn().mockResolvedValue({
    data: undefined,
    error: undefined,
    response: new Response(null, { status: 403 }),
  });
  vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({ PUT: put } as never);

  const qc = new QueryClient({ defaultOptions: { mutations: { retry: false } } });
  const wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={qc}>{children}</QueryClientProvider>
  );
  render(
    <AddSystemMemberDialog open onOpenChange={vi.fn()} system={{ id: "sys1", displayName: "Payments" }} />,
    { wrapper },
  );

  fireEvent.change(screen.getByRole("combobox"), { target: { value: "Che" } });
  await waitFor(() => expect(screen.getByText("Checkout")).toBeInTheDocument());
  fireEvent.click(screen.getByText("Checkout"));

  await waitFor(() =>
    expect(toast.error).toHaveBeenCalledWith(
      "You can only move a component out of a System your team stewards.",
    ),
  );
});
