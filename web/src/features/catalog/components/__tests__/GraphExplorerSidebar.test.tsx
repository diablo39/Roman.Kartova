// web/src/features/catalog/components/__tests__/GraphExplorerSidebar.test.tsx
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { GraphExplorerSidebar } from "@/features/catalog/components/GraphExplorerSidebar";

const mockApp = vi.fn();
const mockSvc = vi.fn();
vi.mock("@/features/catalog/api/applications", () => ({ useApplication: (id: string) => mockApp(id) }));
vi.mock("@/features/catalog/api/services", () => ({ useService: (id: string) => mockSvc(id) }));

const appData = { id: "a", displayName: "A App 041", description: "Seeded #42", lifecycle: "Active", teamId: "t1" };

function renderSidebar(props: Partial<Parameters<typeof GraphExplorerSidebar>[0]> = {}) {
  return render(
    <MemoryRouter>
      <GraphExplorerSidebar
        selected={{ kind: "application", id: "a" }}
        depthFromFocus={1}
        isExpanded={() => false}
        atCap={false}
        onToggleExpand={vi.fn()}
        onSetFocus={vi.fn()}
        onClose={vi.fn()}
        {...props}
      />
    </MemoryRouter>,
  );
}

describe("GraphExplorerSidebar", () => {
  beforeEach(() => {
    mockApp.mockReturnValue({ data: appData, isLoading: false, isError: false });
    mockSvc.mockReturnValue({ data: undefined, isLoading: false, isError: false });
  });

  it("renders entity metadata + depth", () => {
    renderSidebar();
    expect(screen.getByText("A App 041")).toBeInTheDocument();
    expect(screen.getByText(/Active/)).toBeInTheDocument();
    expect(screen.getByText(/depth 1/i)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /open page/i })).toHaveAttribute("href", "/catalog/applications/a");
  });

  it("shows Expand when not expanded and calls onToggleExpand with the direction", () => {
    const onToggleExpand = vi.fn();
    renderSidebar({ onToggleExpand });
    fireEvent.click(screen.getByRole("button", { name: /expand dependencies/i }));
    expect(onToggleExpand).toHaveBeenCalledWith("application:a", "out");
  });

  it("shows Collapse when already expanded", () => {
    renderSidebar({ isExpanded: (_n, d) => d === "in" });
    expect(screen.getByRole("button", { name: /collapse dependents/i })).toBeInTheDocument();
  });

  it("disables Expand at cap but leaves Collapse enabled", () => {
    renderSidebar({ atCap: true, isExpanded: (_n, d) => d === "out" });
    expect(screen.getByRole("button", { name: /expand dependents/i })).toBeDisabled();
    expect(screen.getByRole("button", { name: /collapse dependencies/i })).toBeEnabled();
  });

  it("shows an error state but keeps the actions usable", () => {
    mockApp.mockReturnValue({ data: undefined, isLoading: false, isError: true });
    const onToggleExpand = vi.fn();
    renderSidebar({ onToggleExpand });
    expect(screen.getByText(/couldn.t load details/i)).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: /expand dependencies/i }));
    expect(onToggleExpand).toHaveBeenCalledWith("application:a", "out");
  });
});
