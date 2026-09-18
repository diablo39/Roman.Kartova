import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router-dom";

const useEnvironmentMock = vi.fn();
vi.mock("@/features/catalog/api/environments", () => ({
  useEnvironment: (...a: unknown[]) => useEnvironmentMock(...a),
}));

import { EnvironmentDetailPage } from "../EnvironmentDetailPage";

const ENV_ID = "00000000-0000-0000-0000-000000000abc";

// displayName is deliberately NOT "Production" — the type badge renders the Title-cased
// type label ("Production"), which would otherwise collide with a getByText lookup on the
// heading's own displayName.
function baseEnv(overrides: Record<string, unknown> = {}) {
  return {
    id: ENV_ID,
    tenantId: "t",
    displayName: "Prod Environment",
    description: "Primary production environment",
    type: "production",
    region: "eu-west-1",
    cluster: "prod-eu-west-1",
    resourceDetails: { clusterSize: "5" },
    createdByUserId: "00000000-0000-0000-0000-0000000000aa",
    createdAt: "2026-04-30T00:00:00Z",
    version: "v1",
    ...overrides,
  };
}

function renderPage() {
  return render(
    <MemoryRouter initialEntries={[`/catalog/environments/${ENV_ID}`]}>
      <Routes>
        <Route path="/catalog/environments/:id" element={<EnvironmentDetailPage />} />
      </Routes>
    </MemoryRouter>,
  );
}

describe("EnvironmentDetailPage", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it("shows a loading skeleton while the query is in flight", () => {
    useEnvironmentMock.mockReturnValue({ isLoading: true, isError: false, data: undefined });
    renderPage();
    expect(screen.getByTestId("environment-detail-skeleton")).toBeInTheDocument();
  });

  it("renders name, type, region, cluster, resource details, and created info", () => {
    useEnvironmentMock.mockReturnValue({ isLoading: false, isError: false, data: baseEnv() });
    renderPage();

    expect(screen.getByText("Prod Environment")).toBeInTheDocument();
    expect(screen.getByText("Region")).toBeInTheDocument();
    expect(screen.getByText("eu-west-1")).toBeInTheDocument();
    expect(screen.getByText("Cluster")).toBeInTheDocument();
    expect(screen.getByText("prod-eu-west-1")).toBeInTheDocument();
    expect(screen.getByText("clusterSize")).toBeInTheDocument();
    expect(screen.getByText("5")).toBeInTheDocument();
    expect(screen.getByText(ENV_ID)).toBeInTheDocument();
  });

  it("renders a not-found card on 404 / error", () => {
    useEnvironmentMock.mockReturnValue({
      isLoading: false,
      isError: true,
      error: { title: "Not Found", detail: "Environment not found." },
      data: undefined,
    });
    renderPage();

    expect(screen.getByText("Environment not found")).toBeInTheDocument();
    expect(screen.getByText("Environment not found.")).toBeInTheDocument();
  });

  it("renders a generic no-description placeholder when description is empty", () => {
    useEnvironmentMock.mockReturnValue({
      isLoading: false,
      isError: false,
      data: baseEnv({ description: "" }),
    });
    renderPage();

    expect(screen.getByText("No description")).toBeInTheDocument();
  });

  it("renders em-dash placeholders when region/cluster are null and no resource details", () => {
    useEnvironmentMock.mockReturnValue({
      isLoading: false,
      isError: false,
      data: baseEnv({ region: null, cluster: null, resourceDetails: {} }),
    });
    renderPage();

    expect(screen.getByText("No resource details recorded")).toBeInTheDocument();
  });
});
