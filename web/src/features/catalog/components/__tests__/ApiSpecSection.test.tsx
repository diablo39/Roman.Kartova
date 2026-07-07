import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { ApiSpecSection } from "../ApiSpecSection";
import type { ApiResponse } from "@/features/catalog/api/apis";

let specData: { content: string; mediaType: string } | null = null;
vi.mock("@/features/catalog/api/apis", () => ({
  useApiSpec: () => ({ data: specData, isLoading: false, isError: false }),
  useUpsertApiSpec: () => ({ mutateAsync: vi.fn(), isPending: false }),
}));
let perms = new Set<string>(["catalog.apis.register"]);
vi.mock("@/shared/auth/usePermissions", () => ({
  usePermissions: () => ({ hasPermission: (p: string) => perms.has(p) }),
}));

const api = (hasSpec: boolean): ApiResponse =>
  ({ id: "api-1", displayName: "Orders", style: "rest", version: "v1", teamId: "t1", hasSpec } as unknown as ApiResponse);

describe("ApiSpecSection", () => {
  it("shows empty state + Attach when no spec and permitted", () => {
    specData = null; perms = new Set(["catalog.apis.register"]);
    render(<ApiSpecSection api={api(false)} />);
    expect(screen.getByText(/no spec/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /attach spec/i })).toBeInTheDocument();
  });

  it("renders spec content + Replace when a spec exists", () => {
    specData = { content: "channels: {}", mediaType: "application/yaml" }; perms = new Set(["catalog.apis.register"]);
    render(<ApiSpecSection api={api(true)} />);
    expect(screen.getByText("channels: {}")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /replace/i })).toBeInTheDocument();
  });

  it("hides the mutate button without permission", () => {
    specData = null; perms = new Set();
    render(<ApiSpecSection api={api(false)} />);
    expect(screen.queryByRole("button", { name: /attach spec/i })).not.toBeInTheDocument();
  });
});
