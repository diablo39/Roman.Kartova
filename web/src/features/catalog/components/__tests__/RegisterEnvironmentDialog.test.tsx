import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClientProvider, QueryClient } from "@tanstack/react-query";
import { Toaster } from "sonner";

vi.mock("sonner", async (importOriginal) => {
  const mod = await importOriginal<typeof import("sonner")>();
  return {
    ...mod,
    toast: {
      ...mod.toast,
      success: vi.fn(),
      error: vi.fn(),
    },
  };
});

const useAuthMock = vi.fn();
vi.mock("react-oidc-context", () => ({
  useAuth: () => useAuthMock(),
}));

// Mock the register mutation hook — avoids apiClient spy complexity and keeps the test
// focused on the form contract (field collection + schema validation), same pattern as
// RegisterVmDialog.test.tsx.
const mutateAsync = vi.fn();
vi.mock("@/features/catalog/api/environments", () => ({
  useRegisterEnvironment: () => ({
    mutateAsync,
    isPending: false,
  }),
}));

import { RegisterEnvironmentDialog } from "../RegisterEnvironmentDialog";

function setup({
  open = true,
  onOpenChange = vi.fn(),
}: {
  open?: boolean;
  onOpenChange?: (b: boolean) => void;
} = {}) {
  useAuthMock.mockReturnValue({
    isAuthenticated: true,
    user: {
      access_token: "tok",
      profile: {
        sub: "u-1",
        name: "Alice Admin",
        email: "alice@orga.kartova.local",
        tenant_id: "t",
      },
    },
  });

  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={qc}>
      <Toaster />
      <RegisterEnvironmentDialog open={open} onOpenChange={onOpenChange} />
    </QueryClientProvider>,
  );
  return { onOpenChange };
}

describe("RegisterEnvironmentDialog", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("renders displayName, description, type, region, and the Created by pill — no team select", () => {
    setup();
    expect(screen.getByLabelText(/display name/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/description/i)).toBeInTheDocument();
    expect(screen.getByTestId("register-environment-type-select")).toBeInTheDocument();
    expect(screen.getByLabelText(/region/i)).toBeInTheDocument();
    expect(screen.getByText(/alice admin/i)).toBeInTheDocument();
    expect(screen.queryByTestId("register-environment-team-select")).toBeNull();
    expect(screen.queryByText(/team is required/i)).toBeNull();
  });

  it("renders the environment type options", () => {
    setup();
    const select = screen.getByTestId("register-environment-type-select");
    expect(screen.getByRole("option", { name: "Development" })).toBeInTheDocument();
    expect(screen.getByRole("option", { name: "Staging" })).toBeInTheDocument();
    expect(screen.getByRole("option", { name: "Production" })).toBeInTheDocument();
    expect(select).toHaveValue("development");
  });

  it("rejects empty submit with schema validation errors", async () => {
    setup();

    await userEvent.click(screen.getByRole("button", { name: /register environment/i }));

    expect(await screen.findByText(/name is required/i)).toBeInTheDocument();
    expect(screen.getByText(/description is required/i)).toBeInTheDocument();
    expect(mutateAsync).not.toHaveBeenCalled();
  });

  it("submits a typed RegisterEnvironmentRequest body with resourceDetails: null when all fields are filled", async () => {
    mutateAsync.mockResolvedValue({});
    setup();

    await userEvent.type(screen.getByLabelText(/display name/i), "Production");
    await userEvent.type(screen.getByLabelText(/description/i), "Primary production environment");
    await userEvent.selectOptions(screen.getByTestId("register-environment-type-select"), "production");
    await userEvent.type(screen.getByLabelText(/region/i), "eu-west-1");

    await userEvent.click(screen.getByRole("button", { name: /register environment/i }));

    await waitFor(() => expect(mutateAsync).toHaveBeenCalledTimes(1));
    expect(mutateAsync).toHaveBeenCalledWith({
      displayName: "Production",
      description: "Primary production environment",
      type: "production",
      region: "eu-west-1",
      resourceDetails: null,
    });
  });

  it("submits region as null when left blank", async () => {
    mutateAsync.mockResolvedValue({});
    setup();

    await userEvent.type(screen.getByLabelText(/display name/i), "Staging");
    await userEvent.type(screen.getByLabelText(/description/i), "Staging environment");

    await userEvent.click(screen.getByRole("button", { name: /register environment/i }));

    await waitFor(() => expect(mutateAsync).toHaveBeenCalledTimes(1));
    expect(mutateAsync).toHaveBeenCalledWith(
      expect.objectContaining({ region: null, resourceDetails: null }),
    );
  });

  it("maps a 409 environment-name-conflict onto the displayName field and keeps the dialog open", async () => {
    const onOpenChange = vi.fn();
    mutateAsync.mockRejectedValue({
      type: "https://kartova.io/problems/environment-name-conflict",
      title: "Environment name already in use",
      status: 409,
      detail: "An environment named 'Production' already exists in this tenant.",
    });
    setup({ onOpenChange });

    await userEvent.type(screen.getByLabelText(/display name/i), "Production");
    await userEvent.type(screen.getByLabelText(/description/i), "Primary production environment");

    await userEvent.click(screen.getByRole("button", { name: /register environment/i }));

    expect(
      await screen.findByText(/an environment named 'production' already exists in this tenant/i),
    ).toBeInTheDocument();
    expect(onOpenChange).not.toHaveBeenCalledWith(false);
  });
});
