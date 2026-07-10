import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ApiSpecSection } from "../ApiSpecSection";
import type { ApiResponse } from "@/features/catalog/api/apis";

let specData: { content: string; mediaType: string } | null = null;
let specIsError = false;
vi.mock("@/features/catalog/api/apis", () => ({
  useApiSpec: () => ({ data: specData, isLoading: false, isError: specIsError }),
  useUpsertApiSpec: () => ({ mutateAsync: vi.fn(), isPending: false }),
}));
let perms = new Set<string>(["catalog.apis.register"]);
vi.mock("@/shared/auth/usePermissions", () => ({
  usePermissions: () => ({ hasPermission: (p: string) => perms.has(p) }),
}));
vi.mock("../spec/SpecRender", () => ({
  default: () => <div data-testid="rendered-openapi" />,
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

  it("shows an error message + still renders Replace when the spec fails to load", () => {
    specData = null; specIsError = true; perms = new Set(["catalog.apis.register"]);
    render(<ApiSpecSection api={api(true)} />);
    expect(screen.getByText(/couldn't load the spec/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /replace/i })).toBeInTheDocument();
    specIsError = false;
  });

  it("shows a fallback message instead of a blank gap when hasSpec is true but data is null", () => {
    specData = null; specIsError = false; perms = new Set(["catalog.apis.register"]);
    render(<ApiSpecSection api={api(true)} />);
    expect(screen.getByText(/spec unavailable/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /replace/i })).toBeInTheDocument();
  });

  it("defaults to a rendered view with a toggle when the spec is OpenAPI", async () => {
    specData = { content: '{"openapi":"3.0.0","info":{}}', mediaType: "application/json" };
    perms = new Set(["catalog.apis.register"]);
    render(<ApiSpecSection api={api(true)} />);
    expect(await screen.findByTestId("rendered-openapi")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /raw/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /rendered/i })).toBeInTheDocument();
  });

  it("flips to raw source and back via the toggle", async () => {
    const user = userEvent.setup();
    specData = { content: '{"openapi":"3.0.0","info":{}}', mediaType: "application/json" };
    render(<ApiSpecSection api={api(true)} />);
    await user.click(screen.getByRole("button", { name: /raw/i }));
    expect(screen.getByText(/"openapi":"3.0.0"/)).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: /rendered/i }));
    expect(await screen.findByTestId("rendered-openapi")).toBeInTheDocument();
  });

  it("defaults to a rendered view with a toggle for AsyncAPI too (Scalar renders it)", async () => {
    specData = { content: '{"asyncapi":"3.0.0","info":{}}', mediaType: "application/json" };
    perms = new Set(["catalog.apis.register"]);
    render(<ApiSpecSection api={api(true)} />);
    expect(await screen.findByTestId("rendered-openapi")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /rendered/i })).toBeInTheDocument();
  });

  it("shows raw only (no toggle) for a non-renderable spec (e.g. GraphQL SDL)", () => {
    specData = { content: "type Query { hello: String }", mediaType: "text/plain" };
    render(<ApiSpecSection api={api(true)} />);
    expect(screen.getByText(/type Query/)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /rendered/i })).not.toBeInTheDocument();
  });
});
