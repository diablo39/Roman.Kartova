# OpenAPI Spec Render Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Render an API's stored OpenAPI spec as read-only, browsable documentation on the API detail page, with a toggle back to raw source and a safe fallback for non-OpenAPI/malformed specs.

**Architecture:** Frontend-only. A pure `detectSpecKind` helper classifies the stored content; an `OpenApiRender` wrapper encapsulates the Scalar library behind an error boundary; `ApiSpecSection` gains a Rendered⇄Raw toggle and lazy-loads the renderer. No backend, contract, or permission change.

**Tech Stack:** React + TypeScript, Vite, Vitest + @testing-library/react, Tailwind v4 + Untitled UI, `@scalar/api-reference-react`.

**Spec:** `docs/superpowers/specs/2026-07-10-openapi-spec-render-design.md`

## Global Constraints

- **Frontend-only** — no `src/**` C# change, no contract/OpenAPI-snapshot change, no new `KartovaPermission`.
- **Tailwind v4** (ADR-0094): the Scalar `@layer` order declaration MUST precede `@import "tailwindcss";` in `web/src/index.css`.
- **Never blank-page** (ADR-0084): every render path degrades to raw `<pre>`; verify in a real browser at gate-10.
- **Lazy-load** the Scalar bundle (~200KB+) — it must not enter the main app chunk.
- **Read-only** — no try-it-out; no live request execution this slice.
- Test runner: `cd web && npm run test` (Vitest). Type gate: `npm run build` (`tsc -b && vite build`).
- New render units live together under `web/src/features/catalog/components/openapi/`.

## Impact Analysis (codelens)

**N/A — frontend-only slice.** No existing C# symbol's signature or behavior changes; no backend, contract, or shared-const edit. The sole backend touchpoint (`GET /catalog/apis/{id}/spec`) is consumed unchanged via the existing `useApiSpec` hook. Blast radius is confined to `web/src/features/catalog/components/ApiSpecSection.tsx` and the new `components/openapi/` sibling files — no C# call/reference graph to analyze.

---

### Task 1: Add Scalar dependency + Tailwind v4 layer declaration

**Files:**
- Modify: `web/package.json` (dependency), `web/package-lock.json`
- Modify: `web/src/index.css:1` (prepend `@layer` order)

**Interfaces:**
- Produces: `@scalar/api-reference-react` available for import; CSS layer order established so Tailwind utilities win over Scalar base styles.

- [ ] **Step 1: Install the dependency**

```bash
cd web && npm install @scalar/api-reference-react
```

Expected: `@scalar/api-reference-react` added to `dependencies` in `package.json`; `package-lock.json` updated. (If the dev server on :5173 is running, stop it first — see memory: npm ci/install can EPERM on lightningcss while vite dev holds node_modules.)

- [ ] **Step 2: Prepend the Scalar layer-order declaration**

Edit `web/src/index.css` so the FIRST line is the layer declaration, before the Tailwind import:

```css
@layer scalar-base, scalar-theme, scalar-config, theme, base, components, utilities;
@import "tailwindcss";
@import "./styles/theme.css";
```

Rationale (ADR-0094): declaring Scalar's layers first (lowest priority), then Tailwind's `theme/base/components/utilities`, keeps Tailwind utilities and unlayered app CSS winning over Scalar's injected base styles — preventing the Tailwind-v4 ↔ Scalar cascade conflict.

- [ ] **Step 3: Verify the build still compiles**

```bash
cd web && npm run build
```

Expected: PASS (`tsc -b && vite build` succeed, 0 type errors).

- [ ] **Step 4: Commit**

```bash
git add web/package.json web/package-lock.json web/src/index.css
git commit -m "build(web): add @scalar/api-reference-react + Tailwind v4 layer order"
```

---

### Task 2: `detectSpecKind` pure helper (TDD)

**Files:**
- Create: `web/src/features/catalog/components/openapi/detectSpecKind.ts`
- Test: `web/src/features/catalog/components/openapi/__tests__/detectSpecKind.test.ts`

**Interfaces:**
- Produces: `export type SpecKind = "openapi" | "other";`
  `export function detectSpecKind(content: string | null | undefined, mediaType?: string): SpecKind`
  — `"openapi"` iff the content parses/scans as an OpenAPI or Swagger document; deliberately cheap (no YAML dependency); only chooses the default view.

- [ ] **Step 1: Write the failing test**

```ts
import { describe, it, expect } from "vitest";
import { detectSpecKind } from "../detectSpecKind";

describe("detectSpecKind", () => {
  it("classifies JSON OpenAPI 3.x as openapi", () => {
    expect(detectSpecKind('{"openapi":"3.0.1","info":{}}', "application/json")).toBe("openapi");
  });
  it("classifies JSON Swagger 2.0 as openapi", () => {
    expect(detectSpecKind('{"swagger":"2.0","info":{}}', "application/json")).toBe("openapi");
  });
  it("classifies YAML openapi: as openapi", () => {
    expect(detectSpecKind("openapi: 3.1.0\ninfo:\n  title: X", "application/yaml")).toBe("openapi");
  });
  it("classifies YAML swagger: as openapi", () => {
    expect(detectSpecKind("swagger: '2.0'\ninfo: {}", "application/yaml")).toBe("openapi");
  });
  it("classifies AsyncAPI as other (out of scope this slice)", () => {
    expect(detectSpecKind("asyncapi: 3.0.0\nchannels: {}", "application/yaml")).toBe("other");
  });
  it("classifies GraphQL SDL as other", () => {
    expect(detectSpecKind("type Query { hello: String }", "text/plain")).toBe("other");
  });
  it("classifies arbitrary JSON without the key as other", () => {
    expect(detectSpecKind('{"foo":"bar"}', "application/json")).toBe("other");
  });
  it("classifies garbage / empty / null as other", () => {
    expect(detectSpecKind("not a spec at all", "text/plain")).toBe("other");
    expect(detectSpecKind("", "application/json")).toBe("other");
    expect(detectSpecKind(null)).toBe("other");
    expect(detectSpecKind(undefined)).toBe("other");
  });
  it("falls back to head-scan when JSON.parse fails but content is YAML openapi", () => {
    expect(detectSpecKind("openapi: 3.0.0\npaths: {}   # trailing", "application/json")).toBe("openapi");
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd web && npx vitest run src/features/catalog/components/openapi/__tests__/detectSpecKind.test.ts`
Expected: FAIL — cannot resolve `../detectSpecKind`.

- [ ] **Step 3: Write the implementation**

```ts
export type SpecKind = "openapi" | "other";

/**
 * Classify a stored spec document as OpenAPI-shaped or not. Deliberately cheap
 * and dependency-free (no YAML parser): it only chooses the DEFAULT view.
 * Rendering correctness is guaranteed by OpenApiRender's error boundary, not here.
 */
export function detectSpecKind(content: string | null | undefined, _mediaType?: string): SpecKind {
  if (!content || content.trim() === "") return "other";

  // Primary: structured JSON with a top-level openapi/swagger string key.
  try {
    const doc = JSON.parse(content) as Record<string, unknown>;
    if (typeof doc.openapi === "string" || typeof doc.swagger === "string") return "openapi";
    return "other";
  } catch {
    // Not JSON (likely YAML) — cheap head scan of the first ~4 KB.
    const head = content.slice(0, 4096);
    if (/^\s*(openapi|swagger)\s*:/m.test(head)) return "openapi";
    return "other";
  }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd web && npx vitest run src/features/catalog/components/openapi/__tests__/detectSpecKind.test.ts`
Expected: PASS (all cases).

- [ ] **Step 5: Commit**

```bash
git add web/src/features/catalog/components/openapi/detectSpecKind.ts web/src/features/catalog/components/openapi/__tests__/detectSpecKind.test.ts
git commit -m "feat(web): detectSpecKind helper for OpenAPI spec classification"
```

---

### Task 3: `OpenApiRender` — Scalar wrapper + error-boundary fallback (TDD)

**Files:**
- Create: `web/src/features/catalog/components/openapi/OpenApiRender.tsx`
- Test: `web/src/features/catalog/components/openapi/__tests__/OpenApiRender.test.tsx`

**Interfaces:**
- Consumes: `@scalar/api-reference-react` (`ApiReferenceReact`).
- Produces: `export default function OpenApiRender(props: { content: string; mediaType: string; rawFallback: React.ReactNode }): JSX.Element`
  — renders the Scalar reference; on any render error, shows `rawFallback` + a "couldn't render" notice. Default export so it is `React.lazy`-loadable.

- [ ] **Step 1: Write the failing test**

```tsx
import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import OpenApiRender from "../OpenApiRender";

// Scalar is heavy/web-component-ish; mock it. First test: it throws → boundary catches.
vi.mock("@scalar/api-reference-react", () => ({
  ApiReferenceReact: () => {
    throw new Error("scalar boom");
  },
}));

describe("OpenApiRender", () => {
  it("falls back to raw source + notice when the renderer throws", () => {
    render(
      <OpenApiRender content="openapi: 3.0.0" mediaType="application/yaml" rawFallback={<pre>RAW-SOURCE</pre>} />,
    );
    expect(screen.getByText("RAW-SOURCE")).toBeInTheDocument();
    expect(screen.getByText(/couldn't render/i)).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd web && npx vitest run src/features/catalog/components/openapi/__tests__/OpenApiRender.test.tsx`
Expected: FAIL — cannot resolve `../OpenApiRender`.

- [ ] **Step 3: Write the implementation**

```tsx
import { Component, type ReactNode } from "react";
import { ApiReferenceReact } from "@scalar/api-reference-react";
import "@scalar/api-reference-react/style.css";

type Props = { content: string; mediaType: string; rawFallback: ReactNode };
type State = { failed: boolean };

/**
 * Encapsulates the Scalar OpenAPI renderer behind an error boundary. Any parse
 * or render failure degrades to the raw source (ADR-0084: never blank-page).
 * Default export so ApiSpecSection can React.lazy() it and code-split the bundle.
 */
export default class OpenApiRender extends Component<Props, State> {
  state: State = { failed: false };

  static getDerivedStateFromError(): State {
    return { failed: true };
  }

  render() {
    if (this.state.failed) {
      return (
        <div className="space-y-2">
          <p className="text-sm text-warning-primary">Couldn't render this spec — showing source.</p>
          {this.props.rawFallback}
        </div>
      );
    }
    return (
      <div className="scalar-render overflow-auto rounded-md border border-secondary">
        <ApiReferenceReact
          configuration={{
            content: this.props.content,
            // Read-only: no live request execution this slice (spec §1.1, §6).
            hideClientButton: true,
            theme: "default",
          }}
        />
      </div>
    );
  }
}
```

> Note: the exact Scalar config prop names (`content`, `hideClientButton`, `theme`) target the current `@scalar/api-reference-react` API. If the installed version rejects a prop at build time, consult its types and adjust — the error boundary makes a wrong prop degrade to raw rather than crash. Theming (`--scalar-*` → Untitled UI tokens) is refined at gate-10; the `.scalar-render` container is the scoping hook.

- [ ] **Step 4: Run test to verify it passes**

Run: `cd web && npx vitest run src/features/catalog/components/openapi/__tests__/OpenApiRender.test.tsx`
Expected: PASS (boundary catches the thrown mock, raw fallback + notice shown).

- [ ] **Step 5: Verify the build compiles with the real Scalar import**

```bash
cd web && npm run build
```

Expected: PASS. If a config prop is rejected, fix per the note above, re-run.

- [ ] **Step 6: Commit**

```bash
git add web/src/features/catalog/components/openapi/OpenApiRender.tsx web/src/features/catalog/components/openapi/__tests__/OpenApiRender.test.tsx
git commit -m "feat(web): OpenApiRender Scalar wrapper with raw-fallback error boundary"
```

---

### Task 4: Wire Rendered⇄Raw toggle into `ApiSpecSection` (TDD)

**Files:**
- Modify: `web/src/features/catalog/components/ApiSpecSection.tsx`
- Modify: `web/src/features/catalog/components/__tests__/ApiSpecSection.test.tsx`

**Interfaces:**
- Consumes: `detectSpecKind` (Task 2), `OpenApiRender` default export (Task 3, lazy).
- Produces: `ApiSpecSection` renders a Rendered⇄Raw toggle when the spec is OpenAPI-shaped, defaulting to Rendered; raw-only (no toggle) otherwise.

- [ ] **Step 1: Write the failing tests** (append to the existing describe block; mock the lazy render module so jsdom never loads Scalar)

```tsx
// Add near the top-level mocks:
vi.mock("../openapi/OpenApiRender", () => ({
  default: (props: { rawFallback: React.ReactNode }) => <div data-testid="rendered-openapi" />,
}));

// Add inside describe("ApiSpecSection", ...):
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

it("shows raw only (no toggle) for a non-OpenAPI spec", () => {
  specData = { content: "asyncapi: 3.0.0\nchannels: {}", mediaType: "application/yaml" };
  render(<ApiSpecSection api={api(true)} />);
  expect(screen.getByText(/asyncapi: 3.0.0/)).toBeInTheDocument();
  expect(screen.queryByRole("button", { name: /rendered/i })).not.toBeInTheDocument();
});
```

Add the import at the top of the test file: `import userEvent from "@testing-library/user-event";` (if not already present).

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd web && npx vitest run src/features/catalog/components/__tests__/ApiSpecSection.test.tsx`
Expected: FAIL — no toggle buttons / rendered view yet.

- [ ] **Step 3: Implement the toggle + lazy render in `ApiSpecSection.tsx`**

At the top, add imports and the lazy component:

```tsx
import { lazy, Suspense, useState } from "react";
import { detectSpecKind } from "./openapi/detectSpecKind";

const OpenApiRender = lazy(() => import("./openapi/OpenApiRender"));
```

Replace the block that renders `spec.data` (the `<>...<pre>...</pre></>` fragment) with a detection + toggle. The raw `<pre>` + `CopyButton` become the `rawView`; when the spec is OpenAPI, default to the rendered view with a toggle:

```tsx
{spec.data && (() => {
  const kind = detectSpecKind(spec.data.content, spec.data.mediaType);
  const rawView = (
    <>
      <div className="flex items-center gap-2">
        <Badge type="pill-color" color="gray" size="sm">{formatLabel(spec.data.mediaType)}</Badge>
        <CopyButton text={spec.data.content} />
      </div>
      <pre className="max-h-[480px] overflow-auto rounded-md border border-secondary bg-secondary/30 p-3 font-mono text-xs text-primary whitespace-pre-wrap break-words">
        {spec.data.content}
      </pre>
    </>
  );

  if (kind !== "openapi") return rawView;

  return <SpecViews content={spec.data.content} mediaType={spec.data.mediaType} rawView={rawView} />;
})()}
```

Add a small `SpecViews` component in the same file (owns the toggle state):

```tsx
function SpecViews({ content, mediaType, rawView }: { content: string; mediaType: string; rawView: React.ReactNode }) {
  const [view, setView] = useState<"rendered" | "raw">("rendered");
  const tab = (id: "rendered" | "raw", label: string) => (
    <button
      type="button"
      aria-pressed={view === id}
      onClick={() => setView(id)}
      className={`rounded-md px-2.5 py-1 text-xs font-medium ${
        view === id ? "bg-secondary text-primary" : "text-tertiary hover:text-primary"
      }`}
    >
      {label}
    </button>
  );
  return (
    <div className="space-y-2">
      <div className="inline-flex gap-1 rounded-lg border border-secondary p-0.5">
        {tab("rendered", "Rendered")}
        {tab("raw", "Raw")}
      </div>
      {view === "raw" ? (
        rawView
      ) : (
        <Suspense fallback={<p className="text-sm text-tertiary">Loading rendered spec…</p>}>
          <OpenApiRender content={content} mediaType={mediaType} rawFallback={rawView} />
        </Suspense>
      )}
    </div>
  );
}
```

Ensure `React` types are available for `React.ReactNode` (import `type { ReactNode }` and use `ReactNode`, or keep `React.ReactNode` if `React` is imported). Match the file's existing import style.

- [ ] **Step 4: Run the full ApiSpecSection suite**

Run: `cd web && npx vitest run src/features/catalog/components/__tests__/ApiSpecSection.test.tsx`
Expected: PASS — new toggle tests green AND the pre-existing tests (empty state, replace, permission, error, unavailable, `channels: {}` raw render) still green.

- [ ] **Step 5: Commit**

```bash
git add web/src/features/catalog/components/ApiSpecSection.tsx web/src/features/catalog/components/__tests__/ApiSpecSection.test.tsx
git commit -m "feat(web): Rendered/Raw toggle + lazy OpenAPI render in ApiSpecSection (E-11.F-02.S-01)"
```

---

### Task 5: Verify code-split + full frontend gate

**Files:** none (verification task).

- [ ] **Step 1: Full type + build**

```bash
cd web && npm run build
```

Expected: PASS. Inspect the `vite build` output: Scalar must appear as its OWN chunk (e.g. a hashed chunk importing `@scalar`), NOT bundled into the main `index-*.js` entry. If it lands in the main chunk, confirm `OpenApiRender` is imported only via `lazy(() => import(...))` (no static top-level import anywhere).

- [ ] **Step 2: Full frontend test suite + lint**

```bash
cd web && npm run test && npm run lint
```

Expected: PASS (all Vitest specs green, 0 ESLint errors).

- [ ] **Step 3: Commit (only if build config / lockfile changed)**

If no files changed in this task, skip. Otherwise:

```bash
git add -A && git commit -m "chore(web): confirm Scalar code-split + green frontend gate"
```

---

## Post-plan verification (DoD — not code tasks)

Run per CLAUDE.md's eleven gates after Task 5. Slice-specific:
- **Gate-5 (real-seam integration): N/A** — no HTTP/auth/DB/middleware change (documented in spec §7).
- **Gate-6 (mutation): N/A** — no C# Domain/Application change; `detectSpecKind` TS logic covered by truth-table units.
- **Gate-10 (visual): BLOCKING** — cold-start dev server (ADR-0084), authenticate, open an API detail page with an OpenAPI spec (attach one via `AttachApiSpecDialog` if DevSeed lacks one), confirm: rendered reference by default, toggle Rendered→Raw→Rendered, non-OpenAPI spec falls back to raw, light/dark theming acceptable, **0 console errors**, no CSP violation, **XSS spike** (inject `<img src=x onerror=...>` in a spec `description` → confirm Scalar sanitizes, no script exec). Evidence under `docs/superpowers/verification/2026-07-10-openapi-spec-render/`.
- Impact Analysis (codelens): **N/A — frontend-only, no C# symbol change.**
