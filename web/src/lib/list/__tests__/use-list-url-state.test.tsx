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

const boolConfig = {
  ...config,
  booleanFilters: ["includeDecommissioned"] as const,
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

  describe("booleanFilters", () => {
    it("defaults to false when param is absent", () => {
      const { result } = renderHook(
        () => useListUrlState(boolConfig),
        { wrapper: withRouter("/") },
      );
      expect(result.current.booleanFilters.includeDecommissioned).toBe(false);
    });

    it("reads true from URL when param is 'true'", () => {
      const { result } = renderHook(
        () => useListUrlState(boolConfig),
        { wrapper: withRouter("/?includeDecommissioned=true") },
      );
      expect(result.current.booleanFilters.includeDecommissioned).toBe(true);
    });

    it("reads true case-insensitively ('TRUE', 'True')", () => {
      const { result: r1 } = renderHook(
        () => useListUrlState(boolConfig),
        { wrapper: withRouter("/?includeDecommissioned=TRUE") },
      );
      expect(r1.current.booleanFilters.includeDecommissioned).toBe(true);

      const { result: r2 } = renderHook(
        () => useListUrlState(boolConfig),
        { wrapper: withRouter("/?includeDecommissioned=True") },
      );
      expect(r2.current.booleanFilters.includeDecommissioned).toBe(true);
    });

    it("treats non-'true' values as false ('1', 'yes')", () => {
      const { result: r1 } = renderHook(
        () => useListUrlState(boolConfig),
        { wrapper: withRouter("/?includeDecommissioned=1") },
      );
      expect(r1.current.booleanFilters.includeDecommissioned).toBe(false);

      const { result: r2 } = renderHook(
        () => useListUrlState(boolConfig),
        { wrapper: withRouter("/?includeDecommissioned=yes") },
      );
      expect(r2.current.booleanFilters.includeDecommissioned).toBe(false);
    });

    it("setBooleanFilter(name, true) writes 'true' to URL", () => {
      let search = "";
      function Inner() {
        const loc = useLocation();
        search = loc.search;
        return null;
      }
      const { result } = renderHook(
        () => useListUrlState(boolConfig),
        {
          wrapper: ({ children }) => (
            <MemoryRouter initialEntries={["/"]}>
              <Inner />
              {children}
            </MemoryRouter>
          ),
        },
      );

      act(() => { result.current.setBooleanFilter("includeDecommissioned", true); });

      expect(search).toContain("includeDecommissioned=true");
    });

    it("setBooleanFilter(name, false) removes the param from URL", () => {
      let search = "";
      function Inner() {
        const loc = useLocation();
        search = loc.search;
        return null;
      }
      const { result } = renderHook(
        () => useListUrlState(boolConfig),
        {
          wrapper: ({ children }) => (
            <MemoryRouter initialEntries={["/?includeDecommissioned=true"]}>
              <Inner />
              {children}
            </MemoryRouter>
          ),
        },
      );

      act(() => { result.current.setBooleanFilter("includeDecommissioned", false); });

      expect(search).not.toContain("includeDecommissioned");
    });

    it("returns empty record when booleanFilters config is not provided", () => {
      const { result } = renderHook(
        () => useListUrlState(config),
        { wrapper: withRouter("/") },
      );
      expect(result.current.booleanFilters).toEqual({});
    });

    it("combined sort and boolean params resolve independently", () => {
      const { result } = renderHook(
        () => useListUrlState(boolConfig),
        { wrapper: withRouter("/?sortBy=name&sortOrder=asc&includeDecommissioned=true") },
      );
      expect(result.current.sortBy).toBe("name");
      expect(result.current.sortOrder).toBe("asc");
      expect(result.current.booleanFilters.includeDecommissioned).toBe(true);
    });
  });
});
