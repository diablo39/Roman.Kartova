import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useState } from "react";
import { EndpointsEditor } from "../EndpointsEditor";
import type { EndpointInput } from "@/features/catalog/schemas/registerService";

// Stateful wrapper so add/remove mutate real state in the test.
function Harness({ initial = [] as EndpointInput[] }) {
  const [value, setValue] = useState<EndpointInput[]>(initial);
  return <EndpointsEditor value={value} onChange={setValue} />;
}

describe("EndpointsEditor", () => {
  it("adds an endpoint row when 'Add endpoint' is clicked", async () => {
    render(<Harness />);
    expect(screen.queryByLabelText(/endpoint 1 url/i)).toBeNull();
    await userEvent.click(screen.getByRole("button", { name: /add endpoint/i }));
    expect(screen.getByLabelText(/endpoint 1 url/i)).toBeInTheDocument();
  });

  it("removes an endpoint row", async () => {
    render(<Harness initial={[{ url: "https://a.example.com", protocol: "rest" }]} />);
    expect(screen.getByLabelText(/endpoint 1 url/i)).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: /remove endpoint 1/i }));
    expect(screen.queryByLabelText(/endpoint 1 url/i)).toBeNull();
  });

  it("disables 'Add endpoint' at 50 rows", () => {
    const fifty = Array.from({ length: 50 }, (): EndpointInput => ({ url: "https://x.example.com", protocol: "rest" }));
    render(<EndpointsEditor value={fifty} onChange={vi.fn()} />);
    expect(screen.getByRole("button", { name: /add endpoint/i })).toBeDisabled();
  });

  it("renders a per-row URL error from the errors prop", () => {
    render(
      <EndpointsEditor
        value={[{ url: "bad", protocol: "rest" }]}
        onChange={vi.fn()}
        errors={["Endpoint URL must be an absolute URL (include a scheme and host)"]}
      />,
    );
    expect(screen.getByText(/must be an absolute url/i)).toBeInTheDocument();
  });
});
