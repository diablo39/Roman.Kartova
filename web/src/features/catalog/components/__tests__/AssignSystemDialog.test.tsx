import { it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { AssignSystemDialog } from "@/features/catalog/components/AssignSystemDialog";
import * as systems from "@/features/catalog/api/systems";
import * as rel from "@/features/catalog/api/relationships";

const mutateAsync = vi.fn().mockResolvedValue({ systemId: "sys1", systemDisplayName: "Payments" });

function mockDeps(results: { id: string; displayName: string }[]) {
  vi.spyOn(systems, "useSetComponentSystem").mockReturnValue({ mutateAsync, isPending: false } as never);
  vi.spyOn(rel, "useEntitySearch").mockReturnValue({
    data: results.map((r) => ({ kind: "system", id: r.id, displayName: r.displayName })),
    isLoading: false,
  } as never);
}

function renderDialog(currentSystemName: string | null = null) {
  return render(
    <AssignSystemDialog
      open
      onOpenChange={vi.fn()}
      component={{ kind: "application", id: "a1", displayName: "Checkout" }}
      currentSystemName={currentSystemName}
    />,
  );
}

beforeEach(() => {
  vi.restoreAllMocks();
  mutateAsync.mockClear();
});

it("assigns the selected System to the component", async () => {
  mockDeps([{ id: "sys1", displayName: "Payments" }]);
  renderDialog();

  fireEvent.change(screen.getByRole("combobox"), { target: { value: "Pay" } });
  await waitFor(() => expect(screen.getByText("Payments")).toBeInTheDocument());
  fireEvent.click(screen.getByText("Payments"));

  await waitFor(() =>
    expect(mutateAsync).toHaveBeenCalledWith({ componentKind: "application", componentId: "a1", systemId: "sys1" }),
  );
});

it("clears the membership from the Remove action when already assigned", async () => {
  mockDeps([]);
  renderDialog("Payments");

  fireEvent.click(screen.getByRole("button", { name: /remove from system/i }));

  await waitFor(() =>
    expect(mutateAsync).toHaveBeenCalledWith({ componentKind: "application", componentId: "a1", systemId: null }),
  );
});

it("offers no Remove action when the component is unassigned", () => {
  mockDeps([]);
  renderDialog(null);

  expect(screen.queryByRole("button", { name: /remove from system/i })).toBeNull();
});
