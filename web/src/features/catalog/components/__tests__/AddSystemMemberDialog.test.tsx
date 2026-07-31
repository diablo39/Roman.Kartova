import { it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";

vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));
import { toast } from "sonner";

import { AddSystemMemberDialog } from "@/features/catalog/components/AddSystemMemberDialog";
import * as systems from "@/features/catalog/api/systems";
import * as rel from "@/features/catalog/api/relationships";

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
