import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { AddRelationshipDialog } from "@/features/catalog/components/AddRelationshipDialog";
import * as api from "@/features/catalog/api/relationships";

vi.mock("@/features/catalog/components/EntitySearchCombobox", () => ({
  EntitySearchCombobox: ({ kind, onSelect }: { kind: string; onSelect: (e: unknown) => void }) => (
    <button type="button" onClick={() => onSelect({ kind, id: "e9", displayName: "Picked" })}>
      pick-entity
    </button>
  ),
}));

vi.mock("sonner", () => ({
  toast: {
    success: vi.fn(),
    error: vi.fn(),
  },
}));

vi.mock("@/components/application/modals/modal", () => ({
  ModalOverlay: ({ children, isOpen }: { children: React.ReactNode; isOpen: boolean }) =>
    isOpen ? <div>{children}</div> : null,
  Modal: ({ children }: { children: React.ReactNode }) => <div>{children}</div>,
  Dialog: ({ children }: { children: React.ReactNode; "aria-label"?: string }) => (
    <div>{children}</div>
  ),
}));

const svc = { kind: "service" as const, id: "s1", displayName: "Payments" };

function harness(ui: React.ReactElement) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={qc}>{ui}</QueryClientProvider>);
}

beforeEach(() => {
  vi.clearAllMocks();
});

describe("AddRelationshipDialog", () => {
  it("submits with correct payload when source role", async () => {
    const mutateAsync = vi.fn().mockResolvedValue({ id: "r1" });
    vi.spyOn(api, "useCreateRelationship").mockReturnValue({
      mutateAsync,
      isPending: false,
    } as never);

    const onOpenChange = vi.fn();
    harness(
      <AddRelationshipDialog
        open
        onOpenChange={onOpenChange}
        fixedRole="source"
        fixedEntity={svc}
      />,
    );

    // DependsOn default; pick (stubbed) Application target then submit.
    fireEvent.click(screen.getByText("pick-entity"));
    fireEvent.click(screen.getByRole("button", { name: /add relationship/i }));

    await waitFor(() =>
      expect(mutateAsync).toHaveBeenCalledWith({
        sourceKind: "service",
        sourceId: "s1",
        type: "dependsOn",
        targetKind: "application",
        targetId: "e9",
      }),
    );
    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false));
  });

  it("toasts on 409 duplicate", async () => {
    const { toast } = await import("sonner");
    const errSpy = vi.spyOn(toast, "error").mockImplementation(() => "" as never);
    const mutateAsync = vi
      .fn()
      .mockRejectedValue({ status: 409, detail: "This relationship already exists." });
    vi.spyOn(api, "useCreateRelationship").mockReturnValue({
      mutateAsync,
      isPending: false,
    } as never);

    harness(
      <AddRelationshipDialog
        open
        onOpenChange={vi.fn()}
        fixedRole="source"
        fixedEntity={svc}
      />,
    );

    fireEvent.click(screen.getByText("pick-entity"));
    fireEvent.click(screen.getByRole("button", { name: /add relationship/i }));
    await waitFor(() => expect(errSpy).toHaveBeenCalled());
  });

  it("shows validation error when no entity selected", async () => {
    const mutateAsync = vi.fn();
    vi.spyOn(api, "useCreateRelationship").mockReturnValue({
      mutateAsync,
      isPending: false,
    } as never);

    harness(
      <AddRelationshipDialog
        open
        onOpenChange={vi.fn()}
        fixedRole="source"
        fixedEntity={svc}
      />,
    );

    fireEvent.click(screen.getByRole("button", { name: /add relationship/i }));
    await waitFor(() => expect(screen.getByText(/select a/i)).toBeInTheDocument());
    expect(mutateAsync).not.toHaveBeenCalled();
  });

  it("submits with the fixed entity on the target side when target role", async () => {
    const mutateAsync = vi.fn().mockResolvedValue({ id: "r1" });
    vi.spyOn(api, "useCreateRelationship").mockReturnValue({
      mutateAsync,
      isPending: false,
    } as never);

    harness(
      <AddRelationshipDialog
        open
        onOpenChange={vi.fn()}
        fixedRole="target"
        fixedEntity={svc}
      />,
    );

    fireEvent.click(screen.getByText("pick-entity"));
    fireEvent.click(screen.getByRole("button", { name: /add relationship/i }));

    await waitFor(() =>
      expect(mutateAsync).toHaveBeenCalledWith({
        sourceKind: "application",
        sourceId: "e9",
        type: "dependsOn",
        targetKind: "service",
        targetId: "s1",
      }),
    );
  });

  it("offers DependsOn only when target role with a Service fixed entity", () => {
    vi.spyOn(api, "useCreateRelationship").mockReturnValue({
      mutateAsync: vi.fn(),
      isPending: false,
    } as never);

    harness(
      <AddRelationshipDialog
        open
        onOpenChange={vi.fn()}
        fixedRole="target"
        fixedEntity={svc}
      />,
    );

    const typeSelect = screen.getByTestId("relationship-type-select") as HTMLSelectElement;
    expect(Array.from(typeSelect.options).map((o) => o.value)).toEqual(["dependsOn"]);
  });

  it("offers API edge types from an application source and posts an api target", async () => {
    const mutateAsync = vi.fn().mockResolvedValue({ id: "r1" });
    vi.spyOn(api, "useCreateRelationship").mockReturnValue({ mutateAsync, isPending: false } as never);
    harness(
      <AddRelationshipDialog open onOpenChange={vi.fn()} fixedRole="source"
        fixedEntity={{ kind: "application", id: "a1", displayName: "Checkout" }} />,
    );
    const typeSelect = screen.getByTestId("relationship-type-select") as HTMLSelectElement;
    expect(Array.from(typeSelect.options).map((o) => o.value)).toEqual(["dependsOn", "providesApiFor", "consumesApiFrom"]);
    fireEvent.change(typeSelect, { target: { value: "providesApiFor" } });
    fireEvent.click(screen.getByText("pick-entity"));
    fireEvent.click(screen.getByRole("button", { name: /add relationship/i }));
    await waitFor(() =>
      expect(mutateAsync).toHaveBeenCalledWith({
        sourceKind: "application", sourceId: "a1", type: "providesApiFor", targetKind: "api", targetId: "e9",
      }),
    );
  });

  it("offers instanceOf from a service source", () => {
    vi.spyOn(api, "useCreateRelationship").mockReturnValue({ mutateAsync: vi.fn(), isPending: false } as never);
    harness(
      <AddRelationshipDialog open onOpenChange={vi.fn()} fixedRole="source" fixedEntity={svc} />,
    );
    const typeSelect = screen.getByTestId("relationship-type-select") as HTMLSelectElement;
    expect(Array.from(typeSelect.options).map((o) => o.value)).toEqual(["dependsOn", "instanceOf", "providesApiFor", "consumesApiFrom"]);
  });

  it("restrictTypes limits the offered types and locks the select to a single API type", () => {
    vi.spyOn(api, "useCreateRelationship").mockReturnValue({ mutateAsync: vi.fn(), isPending: false } as never);
    harness(
      <AddRelationshipDialog open onOpenChange={vi.fn()} fixedRole="source"
        fixedEntity={{ kind: "application", id: "a1", displayName: "Checkout" }}
        restrictTypes={["providesApiFor"]} heading="Add provided API" />,
    );
    const typeSelect = screen.getByTestId("relationship-type-select") as HTMLSelectElement;
    expect(Array.from(typeSelect.options).map((o) => o.value)).toEqual(["providesApiFor"]);
    expect(typeSelect.disabled).toBe(true);
    expect(screen.getByText("Add provided API")).toBeInTheDocument();
  });

  it("restrictTypes excludes API types for the Relationships dialog", () => {
    vi.spyOn(api, "useCreateRelationship").mockReturnValue({ mutateAsync: vi.fn(), isPending: false } as never);
    harness(
      <AddRelationshipDialog open onOpenChange={vi.fn()} fixedRole="source" fixedEntity={svc}
        restrictTypes={["dependsOn", "instanceOf"]} />,
    );
    const typeSelect = screen.getByTestId("relationship-type-select") as HTMLSelectElement;
    expect(Array.from(typeSelect.options).map((o) => o.value)).toEqual(["dependsOn", "instanceOf"]);
  });

  it("uses generic outgoing/incoming titles (not dependency-specific)", () => {
    vi.spyOn(api, "useCreateRelationship").mockReturnValue({ mutateAsync: vi.fn(), isPending: false } as never);
    const { rerender } = harness(
      <AddRelationshipDialog open onOpenChange={vi.fn()} fixedRole="source" fixedEntity={svc} />,
    );
    expect(screen.getByText("Add outgoing relationship")).toBeInTheDocument();
    rerender(<AddRelationshipDialog open onOpenChange={vi.fn()} fixedRole="target" fixedEntity={svc} />);
    expect(screen.getByText("Add incoming relationship")).toBeInTheDocument();
  });
});
