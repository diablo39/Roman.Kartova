// web/src/features/catalog/components/__tests__/GraphExplorerSidebar.test.tsx
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { GraphExplorerSidebar } from "@/features/catalog/components/GraphExplorerSidebar";

const mockApp = vi.fn();
const mockSvc = vi.fn();
const mockApi = vi.fn();
vi.mock("@/features/catalog/api/applications", () => ({ useApplication: (id: string) => mockApp(id) }));
vi.mock("@/features/catalog/api/services", () => ({ useService: (id: string) => mockSvc(id) }));
vi.mock("@/features/catalog/api/apis", () => ({ useApi: (id: string) => mockApi(id) }));

const appData = { id: "a", displayName: "A App 041", description: "Seeded #42", lifecycle: "Active", teamId: "t1" };
const apiData = { id: "api-1", displayName: "Orders API" };

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
    mockApi.mockReturnValue({ data: undefined, isLoading: false, isError: false });
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

  it("renders an api-kind selected node with the API label and detail link", () => {
    mockApi.mockReturnValue({ data: apiData, isLoading: false, isError: false });
    renderSidebar({ selected: { kind: "api", id: "api-1" } });
    expect(screen.getByText("Orders API")).toBeInTheDocument();
    expect(screen.getByText("API")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /open page/i })).toHaveAttribute("href", "/catalog/apis/api-1");
  });

  it("shows an error state but keeps the actions usable", () => {
    mockApp.mockReturnValue({ data: undefined, isLoading: false, isError: true });
    const onToggleExpand = vi.fn();
    renderSidebar({ onToggleExpand });
    expect(screen.getByText(/couldn.t load details/i)).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: /expand dependencies/i }));
    expect(onToggleExpand).toHaveBeenCalledWith("application:a", "out");
  });

  it("shows Impact analysis for service/application and hides it for api", () => {
    const onImpactAnalysis = vi.fn();
    const { rerender } = renderSidebar({ selected: { kind: "service", id: "s1" }, onImpactAnalysis });
    expect(screen.getByRole("button", { name: /impact analysis/i })).toBeInTheDocument();

    mockSvc.mockReturnValue({ data: undefined, isLoading: false, isError: false });
    mockApi.mockReturnValue({ data: apiData, isLoading: false, isError: false });
    rerender(
      <MemoryRouter>
        <GraphExplorerSidebar
          selected={{ kind: "api", id: "a1" }}
          depthFromFocus={1}
          isExpanded={() => false}
          atCap={false}
          onToggleExpand={vi.fn()}
          onSetFocus={vi.fn()}
          onClose={vi.fn()}
          onImpactAnalysis={onImpactAnalysis}
        />
      </MemoryRouter>,
    );
    expect(screen.queryByRole("button", { name: /impact analysis/i })).toBeNull();
  });
});
