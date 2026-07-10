# Slice — Catalog: Render OpenAPI specs as read-only interactive docs

**Date:** 2026-07-10
**Story:** E-11.F-02.S-01 — Render OpenAPI specs as interactive docs (read-only first cut).
**Phase:** 3 — Documentation (pulled forward; the API entity + spec storage already shipped in Phase 1/2, so the seam is warm).
**Branch:** `feat/catalog-openapi-spec-render`
**Governing decisions:** [ADR-0111](../../architecture/decisions/ADR-0111-api-first-class-entity-provider-instance-fields.md) (API first-class, unified entity keyed by `Style`), [ADR-0112](../../architecture/decisions/ADR-0112-api-spec-artifacts-stored-in-postgres.md) (spec stored as opaque `text` in `catalog_api_specs`, 1:1, RLS), [ADR-0094](../../architecture/decisions/ADR-0094-untitled-ui-component-library.md) (Untitled UI = react-aria-components + Tailwind v4), [ADR-0084](../../architecture/decisions/ADR-0084-playwright-mcp-for-frontend-development.md) (browser verification; never blank-page).

---

## 1. Goal

The API detail page already fetches and shows a stored spec — as a **raw `<pre>` text dump** (`ApiSpecSection.tsx`). This slice renders an OpenAPI spec as **browsable, formatted, read-only documentation** (endpoints, params, request/response schemas, examples) in place of the raw dump, with a toggle back to the raw source.

**Success criterion:** an API whose stored spec is an OpenAPI 3.x (or Swagger 2.0) document shows a rendered reference by default; a dev can flip to raw source and copy it; any non-OpenAPI or malformed doc keeps today's raw view with no regression and no blank page.

> **Amendment (2026-07-10, folded into PR #69):** AsyncAPI rendering was pulled in from **E-11.F-03.S-01**. A read-only spike proved Scalar renders AsyncAPI (channels/operations/messages) through the *same* component with the *same* read-only enforcement (no CSS change needed). `detectSpecKind` now also matches the top-level `asyncapi` key; the `"openapi"` kind was renamed `"rendered"`, `OpenApiRender`→`SpecRender`, folder `openapi/`→`spec/`. The "Style scope" and "Out of scope" rows below now read **OpenAPI + AsyncAPI**; GraphQL/gRPC remain out of scope. Gate-10 re-verified for both formats.

### 1.1 Scope (locked in brainstorming 2026-07-10)

| Decision | Choice |
|---|---|
| Interactivity | **Read-only now**, try-it-out later. Library chosen so "later" is a config flip, not a rewrite. |
| Style scope | **OpenAPI-shaped only, content-detected** (not keyed off `api.style`, since stored content is opaque/unvalidated and mislabel-prone). gRPC / GraphQL / AsyncAPI / garbage → raw fallback. |
| Presentation | **Toggle Rendered ⇄ Raw source**, default Rendered. Raw = today's `<pre>` + copy, preserved. |
| Library | **Scalar** (`@scalar/api-reference-react`) — see §5. |
| Backend | **No change.** `useApiSpec` already returns `{ content, mediaType }`. |

**Out of scope (explicit):** try-it-out / live request execution (later story); gRPC proto rendering (F-02.S-02); GraphQL SDL rendering; AsyncAPI rendering (F-03.S-01 — becomes cheap: Scalar renders AsyncAPI too, so it's one more detector branch + same component, not a new library); spec versioning (F-02.S-03 / E-21); spec validation on upload (deliberately opaque per ADR-0112).

---

## 2. Pre-requisites (already on master)

- **Spec storage + read:** `ApiSpec` aggregate + `catalog_api_specs` (RLS, 1:1, ADR-0112); `GetApiSpecQuery`; `GET /catalog/apis/{id}/spec` returns raw content + negotiated `Content-Type`.
- **Frontend:** `ApiDetailPage.tsx` → `ApiSpecSection.tsx`; `useApiSpec(id, hasSpec)` returns `{ content, mediaType } | null` (JSON or YAML); `AttachApiSpecDialog` for upload; copy-raw affordance.
- **`ApiResponse`** carries `hasSpec`, `specUrl`, `style`, `version`, `id`, `displayName`.

No backend or contract change → **no OpenAPI-snapshot codegen churn**, no new permission, no 5-sync.

---

## 3. Architecture — frontend-only, encapsulated library

```
ApiDetailPage
  └─ ApiSpecSection            (orchestrates: toggle state + fallback decision)
       ├─ detectSpecKind(content, mediaType)   pure helper → 'openapi' | 'other'
       ├─ [Rendered]  <Suspense><OpenApiRenderLazy content mediaType /></Suspense>
       │                 └─ OpenApiRender       error-boundary + Scalar wrapper (owns the dep)
       └─ [Raw]       existing <pre> + CopyButton   (unchanged)
```

**Units & boundaries:**

| Unit | Responsibility | Depends on | Testable in isolation |
|---|---|---|---|
| `detectSpecKind` | Classify content as OpenAPI-shaped or not; pick the *default* view. Pure, no React. | — | ✅ truth-table unit tests |
| `OpenApiRender` | Wrap `@scalar/api-reference-react`; own theming + the error boundary that falls back to raw. Single point a library swap touches. | Scalar | ✅ error-boundary test (Scalar mocked) |
| `ApiSpecSection` | Fetch spec, run detection, render toggle, choose Rendered/Raw. | the two above | ✅ component tests (OpenApiRender mocked) |

**Lazy-load:** `OpenApiRenderLazy = React.lazy(() => import('./OpenApiRender'))`. Scalar's ~200KB+ bundle is code-split into that chunk and never enters the main app bundle; it loads only when a spec is actually rendered. `<Suspense>` shows a skeleton meanwhile.

---

## 4. Data flow & detection

1. `ApiSpecSection` calls `useApiSpec(api.id, api.hasSpec)` → `{ content, mediaType } | null`.
2. `detectSpecKind(content, mediaType)`:
   - Try `JSON.parse(content)` → object with `openapi` or `swagger` string key ⇒ `'openapi'`.
   - On JSON parse failure (likely YAML) → scan the first ~4 KB for `/^\s*(openapi|swagger)\s*:/m` ⇒ `'openapi'`.
   - Else ⇒ `'other'`. Empty/null ⇒ `'other'`.
   - **Detection only chooses the default view.** It is deliberately cheap and dependency-free (no YAML parser added); correctness of *rendering* is guaranteed by the error boundary, not by detection.
3. `'openapi'` → toggle rendered, default **Rendered**: mount `OpenApiRender` (lazy). Scalar consumes `{ content }` and parses JSON/YAML itself.
4. `'other'` → **raw only, no toggle** (exact current behavior).
5. Toggle flips view state locally (`useState`); Raw path is the untouched `<pre>` + copy.

---

## 5. Library — Scalar (`@scalar/api-reference-react`)

Chosen over Swagger UI / RapiDoc / Redoc (brainstorming 2026-07-10):
- Read-only quality (modern 2-pane, dark mode) **and native try-it-out** → satisfies "later" with no rewrite.
- CSS-variable theming (`--scalar-*`) maps cleanly to Untitled UI tokens.
- ~3–4× lighter than Swagger UI; MIT; OpenAPI 3.0/3.1 + Swagger 2.0; JSON + YAML.
- **Stack consistency:** Microsoft made Scalar the default API-reference renderer in ASP.NET Core 9 — likely already Kartova's own-API dogfood renderer.
- **Bonus:** renders AsyncAPI too (2026, first-class) → makes F-03.S-01 a cheap extension of this component.

**Documented fallback:** Swagger UI (`swagger-ui-react`) if the theming spike (§8, R1) fails — `OpenApiRender` is the single swap point.

---

## 6. Theming, error handling & security

- **Tailwind v4 layer conflict (must-fix, ADR-0094):** declare `@layer scalar-base, scalar-theme, scalar-config, theme, base, components, utilities;` before the Tailwind import so Scalar's styles don't override / get overridden unpredictably. Scope Scalar styles to the render container.
- **Theming:** map Untitled UI color/font tokens → `--scalar-*` custom properties; honor light/dark via the existing `data-theme` mechanism. Prove out in the spike (known React-wrapper theming bugs, GH #3388/#2392).
- **Error boundary (safety net):** `OpenApiRender` wraps Scalar in a class error boundary. Any throw (parse failure, mismatched doc, renderer bug) → render the raw `<pre>` + a subtle "Couldn't render this spec — showing source." notice. Guarantees **no blank page** (ADR-0084 lesson).
- **Loading:** `<Suspense>` skeleton while the lazy chunk + spec resolve. `useApiSpec` error/loading states kept.
- **Security:**
  - Spec is **tenant-supplied opaque text**. OpenAPI `description`/`summary` fields accept CommonMark + raw HTML → stored-XSS surface. **Spike gate (blocking): verify Scalar sanitizes rendered markup** (it uses a sanitizer; confirm with an injected `<img onerror>` / `<script>` in a description). If insufficient → sanitize before handing to Scalar or fall back.
  - **Try-it-out OFF** → no live-request construction → no SSRF/CORS/auth-injection surface this slice.
  - No `dangerouslySetInnerHTML` in our code. Watch CSP: Scalar injects `<style>`; confirm no inline-script/eval CSP violations at gate-10.

---

## 7. Testing strategy

Frontend-only slice — **no HTTP/auth/DB/middleware seam changes**, so per [docs/TESTING-STRATEGY.md](../../TESTING-STRATEGY.md) the gate-5 real-seam integration artifacts are **N/A** (documented, not skipped silently). Coverage is unit + component + browser:

| Artifact | Cases |
|---|---|
| `detectSpecKind` unit (truth table) | JSON openapi 3.x ⇒ openapi; JSON swagger 2.0 ⇒ openapi; YAML `openapi:` ⇒ openapi; YAML `swagger:` ⇒ openapi; gRPC/GraphQL SDL/AsyncAPI/plain-text/garbage/empty/null ⇒ other; malformed JSON that is really YAML ⇒ heuristic openapi; JSON object lacking the key ⇒ other |
| `ApiSpecSection` component | openapi content ⇒ toggle present + default Rendered (OpenApiRender mounted, mocked); non-openapi ⇒ raw only, no toggle; toggle flips Rendered→Raw→Rendered; copy button preserved on Raw; loading/error states of `useApiSpec` surfaced |
| `OpenApiRender` error boundary | Scalar mocked to throw ⇒ raw fallback + notice, no blank render |

Scalar is heavy/web-component-ish and does not render meaningfully in jsdom → **mocked** in unit/component tests; real render is proven at gate-10.

- **Gate-6 (mutation):** **N/A** — no C# Domain/Application change; `detectSpecKind` is TypeScript (Stryker.NET does not cover TS) and is fully exercised by the truth-table units. Recorded as N/A-with-reason.
- **Gate-10 (visual, blocking):** cold-start dev server (ADR-0084), authenticate, open an API detail page whose spec is an OpenAPI doc → confirm rendered reference, toggle to raw + back, non-OpenAPI/garbage falls back cleanly, light/dark theming, **0 console errors**, no CSP violation. Seed note: DevSeed may not include an API with an OpenAPI spec attached — if not, attach one via `AttachApiSpecDialog` during verification (evidence: screenshot rendered + raw).

---

## 8. Risks

| # | Risk | Mitigation |
|---|---|---|
| R1 | Scalar React-wrapper theming bugs (GH #3388/#2392) → can't match Untitled UI | Theming spike first; Swagger UI documented fallback; `OpenApiRender` is the single swap point |
| R2 | Tailwind v4 ↔ Scalar CSS-layer conflict | Explicit `@layer` order declaration before Tailwind import (§6); verify in browser |
| R3 | Stored-XSS via OpenAPI description HTML | Blocking spike gate: verify Scalar sanitization with injected payload (§6) |
| R4 | Bundle bloat in main chunk | `React.lazy` code-split; assert Scalar absent from main chunk in build output |
| R5 | Blank-page on malformed spec (ADR-0084) | Error boundary → raw fallback; browser-verify at gate-10 |
| R6 | Scalar CSS/JS leaks out of the render container | Scope styles to container; confirm no global bleed at gate-10 |

---

## 9. Impact Analysis (codelens)

**N/A — frontend-only slice.** No existing C# symbol signature or behavior changes; no backend, contract, or shared-const edit. The only backend touchpoint (`GET /catalog/apis/{id}/spec`) is consumed unchanged. Blast radius is confined to `web/src/features/catalog/components/ApiSpecSection.tsx` and two new sibling files.

---

## 10. Definition of Done

The eleven CLAUDE.md gates apply (linked, not restated). Slice-specific notes: gate-5 **N/A** (no real seam changed); gate-6 **N/A** (no C# Domain/Application change); gate-10 **blocking** (browser render + fallback + theming). DoD ledger at `docs/superpowers/verification/2026-07-10-openapi-spec-render/dod.md`; findings log at `gate-findings.yaml`.
