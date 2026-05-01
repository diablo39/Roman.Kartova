import { describe, it, expect, vi, beforeEach } from "vitest";
import { renderHook } from "@testing-library/react";

const useAuthMock = vi.fn();

vi.mock("react-oidc-context", () => ({
  useAuth: () => useAuthMock(),
}));

import { useCurrentUser } from "../useCurrentUser";

describe("useCurrentUser", () => {
  beforeEach(() => {
    useAuthMock.mockReset();
  });

  it("returns mapped claims when authenticated", () => {
    useAuthMock.mockReturnValue({
      isAuthenticated: true,
      user: {
        access_token: "tok-123",
        profile: {
          sub: "u-1",
          name: "Alice Admin",
          email: "alice@orga.kartova.local",
          tenant_id: "11111111-1111-1111-1111-111111111111",
        },
      },
    });

    const { result } = renderHook(() => useCurrentUser());

    expect(result.current).toEqual({
      userId: "u-1",
      displayName: "Alice Admin",
      email: "alice@orga.kartova.local",
      tenantId: "11111111-1111-1111-1111-111111111111",
      accessToken: "tok-123",
    });
  });

  it("returns null when not authenticated", () => {
    useAuthMock.mockReturnValue({ isAuthenticated: false, user: undefined });

    const { result } = renderHook(() => useCurrentUser());

    expect(result.current).toBeNull();
  });

  it("falls back to preferred_username then email when name claim is missing", () => {
    useAuthMock.mockReturnValue({
      isAuthenticated: true,
      user: {
        access_token: "tok",
        profile: {
          sub: "u-2",
          preferred_username: "bob",
          email: "bob@x.test",
          tenant_id: "t",
        },
      },
    });

    const { result } = renderHook(() => useCurrentUser());

    expect(result.current?.displayName).toBe("bob");
  });
});
