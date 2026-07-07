import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { AttachApiSpecDialog, inferMediaType } from "../AttachApiSpecDialog";

const mutateAsync = vi.fn();
vi.mock("@/features/catalog/api/apis", () => ({
  useUpsertApiSpec: () => ({ mutateAsync, isPending: false }),
}));
vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

function renderDialog(props: Partial<Parameters<typeof AttachApiSpecDialog>[0]> = {}) {
  const qc = new QueryClient();
  return render(
    <QueryClientProvider client={qc}>
      <AttachApiSpecDialog apiId="api-1" open onOpenChange={() => {}} hasExistingSpec={false} {...props} />
    </QueryClientProvider>,
  );
}

beforeEach(() => {
  mutateAsync.mockReset().mockResolvedValue(undefined);
});

describe("inferMediaType", () => {
  it("infers from file extension and content sniff", () => {
    expect(inferMediaType("openapi.yaml", "")).toBe("application/yaml");
    expect(inferMediaType("openapi.yml", "")).toBe("application/yaml");
    expect(inferMediaType("openapi.json", "")).toBe("application/json");
    expect(inferMediaType(undefined, "{\"a\":1}")).toBe("application/json");
    expect(inferMediaType(undefined, "openapi: 3.0.0\ninfo: {}")).toBe("application/yaml");
  });
});

describe("AttachApiSpecDialog", () => {
  it("submits pasted content with inferred media type", async () => {
    renderDialog();
    await userEvent.type(screen.getByLabelText(/paste/i), "{{\"openapi\":\"3.0.0\"}");
    await userEvent.click(screen.getByRole("button", { name: /attach spec/i }));
    await waitFor(() =>
      expect(mutateAsync).toHaveBeenCalledWith({
        content: "{\"openapi\":\"3.0.0\"}",
        mediaType: "application/json",
      }),
    );
  });

  it("blocks submit on empty content", async () => {
    renderDialog();
    await userEvent.click(screen.getByRole("button", { name: /attach spec/i }));
    expect(mutateAsync).not.toHaveBeenCalled();
    expect(screen.getByText(/must not be empty/i)).toBeInTheDocument();
  });

  it("surfaces a 415 ProblemDetails error", async () => {
    mutateAsync.mockRejectedValue({ status: 415, detail: "Only JSON or YAML content is supported" });
    renderDialog();
    await userEvent.type(screen.getByLabelText(/paste/i), "not json");
    await userEvent.click(screen.getByRole("button", { name: /attach spec/i }));
    await waitFor(() => expect(screen.getByText(/only json or yaml/i)).toBeInTheDocument());
  });

  it("titles Replace when spec exists", () => {
    renderDialog({ hasExistingSpec: true });
    expect(screen.getByRole("heading", { name: /replace spec/i })).toBeInTheDocument();
  });

  it("preserves a manual media-type override while continuing to type", async () => {
    renderDialog();
    await userEvent.type(screen.getByLabelText(/paste/i), "{{\"openapi\":\"3.0.0\"}");
    await userEvent.selectOptions(screen.getByTestId("spec-media-type-select"), "application/yaml");
    await userEvent.type(screen.getByLabelText(/paste/i), "\nmore content");
    await userEvent.click(screen.getByRole("button", { name: /attach spec/i }));
    await waitFor(() =>
      expect(mutateAsync).toHaveBeenCalledWith({
        content: "{\"openapi\":\"3.0.0\"}\nmore content",
        mediaType: "application/yaml",
      }),
    );
  });
});
