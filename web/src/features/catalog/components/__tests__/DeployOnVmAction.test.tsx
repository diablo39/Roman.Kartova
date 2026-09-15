import { describe, it, expect, vi } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";

// Stub DeployOnVmDialog so the dialog-open assertion doesn't pull in EntitySearchCombobox/react-query.
vi.mock("@/features/catalog/components/DeployOnVmDialog", () => ({
  DeployOnVmDialog: (props: { open: boolean; component: { kind: string; id: string; displayName: string } }) =>
    props.open ? (
      <div data-testid="deploy-on-vm-dialog">
        Deploy {props.component.displayName} on a VM ({props.component.kind})
      </div>
    ) : null,
}));

import { DeployOnVmAction } from "@/features/catalog/components/DeployOnVmAction";

describe("DeployOnVmAction", () => {
  it("renders nothing when canDeploy is false", () => {
    render(<DeployOnVmAction kind="application" id="a1" displayName="Checkout" canDeploy={false} />);

    expect(screen.queryByRole("button", { name: /deploy on vm/i })).not.toBeInTheDocument();
  });

  it("renders the button when canDeploy is true", () => {
    render(<DeployOnVmAction kind="service" id="s1" displayName="Billing" canDeploy />);

    expect(screen.getByRole("button", { name: /deploy on vm/i })).toBeInTheDocument();
    expect(screen.queryByTestId("deploy-on-vm-dialog")).not.toBeInTheDocument();
  });

  it("opens the dialog for the given component when the button is clicked", () => {
    render(<DeployOnVmAction kind="application" id="a1" displayName="Checkout" canDeploy />);

    fireEvent.click(screen.getByRole("button", { name: /deploy on vm/i }));

    expect(screen.getByTestId("deploy-on-vm-dialog")).toHaveTextContent(
      "Deploy Checkout on a VM (application)",
    );
  });
});
