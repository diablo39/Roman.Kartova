import { describe, expect, it } from "vitest";
import { renderHook, act } from "@testing-library/react";
import { MemoryRouter, useLocation } from "react-router-dom";
import type { ReactNode } from "react";
import { useListUrlState } from "../useListUrlState";

function withRouter(initial: string) {
  return ({ children }: { children: ReactNode }) => (
    <MemoryRouter initialEntries={[initial]}>{children}</MemoryRouter>
  );
}

const config = {
  defaultSortBy: "createdAt" as const,
  defaultSortOrder: "desc" as const,
  allowedSortFields: ["createdAt", "name"] as const,
};

describe("useListUrlState", () => {
  it("falls back to defaults when URL has no params", () => {
    const { result } = renderHook(() => useListUrlState(config), { wrapper: withRouter("/") });
    expect(result.current.sortBy).toBe("createdAt");
    expect(result.current.sortOrder).toBe("desc");
  });

  it("reads sort from URL when present", () => {
    const { result } = renderHook(
      () => useListUrlState(config),
      { wrapper: withRouter("/?sortBy=name&sortOrder=asc") },
    );
    expect(result.current.sortBy).toBe("name");
    expect(result.current.sortOrder).toBe("asc");
  });

  it("falls back to default when sortBy is not in allowlist", () => {
    const { result } = renderHook(
      () => useListUrlState(config),
      { wrapper: withRouter("/?sortBy=garbage") },
    );
    expect(result.current.sortBy).toBe("createdAt");
  });

  it("setSort updates the URL", () => {
    let pathname = "";
    let search = "";
    function Inner() {
      const loc = useLocation();
      pathname = loc.pathname;
      search = loc.search;
      return null;
    }
    const { result } = renderHook(
      () => {
        const s = useListUrlState(config);
        return s;
      },
      {
        wrapper: ({ children }) => (
          <MemoryRouter initialEntries={["/"]}>
            <Inner />
            {children}
          </MemoryRouter>
        ),
      },
    );

    act(() => { result.current.setSort("name", "asc"); });

    expect(search).toContain("sortBy=name");
    expect(search).toContain("sortOrder=asc");
    expect(pathname).toBe("/");
  });
});
