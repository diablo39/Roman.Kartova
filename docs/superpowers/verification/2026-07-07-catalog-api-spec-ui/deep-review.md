# Deep PR Review — Catalog: API spec UI (attach/view) + configurable size cap

**Branch:** `feat/catalog-api-spec-ui` · **Range:** `a07f59a..8a5a2dc` · **Status:** OPEN (pre-merge gate, DoD gate 9)
**Spec:** `docs/superpowers/specs/2026-07-07-catalog-api-spec-ui-and-configurable-cap-design.md`
**Plan:** `docs/superpowers/plans/2026-07-07-catalog-api-spec-ui-and-configurable-cap.md`
**ADRs read against:** 0112 (spec storage — amended this slice), 0111 (unified API entity), 0107 (list surface), 0094 (Untitled UI), 0090 (tenant scope/RLS).
**Reviewer note:** Ran independently of gates 5/7/8. Items already fixed by the prior whole-branch review (ApiSpecSection `isError` gap, `useApiSpec` `__status`, CopyButton cleanup, media-type override persistence) and the two accepted items (no-token Authorization omission; native `<select>`/file input) are **not** re-reported.

---

### Overview

The slice gives the shipped `PUT`/`GET /apis/{id}/spec` backend a user surface — an inline read-only `ApiSpecSection` (media-type badge + `<pre>` + copy) and a file-or-paste `AttachApiSpecDialog` on the API detail page for all styles, plus a non-sortable `Spec` indicator column on the Apis list — via a raw-`fetch` data layer (`useApiSpec`/`useUpsertApiSpec`) that bypasses openapi-fetch so YAML round-trips intact. On the backend, the hardcoded 5 MiB `ApiSpec.MaxContentBytes` domain const is removed and the cap moves to `IOptions<CatalogSpecOptions>` (bound from `Catalog:ApiSpec`, default 5 MiB, `IValidateOptions` band 1 KiB…50 MiB, `ValidateOnStart`); the upload endpoint reads the configured value at both the declared-length pre-check and the streamed read, and names it in the 400. ADR-0112 is amended (not superseded) and no new permission is introduced.

### Blocking-class issues

None.

The three highest-risk aspects the review was asked to judge all hold:
- **RLS / tenant scope** (ADR-0090): unchanged from S-02. `UpsertApiSpecAsync` loads via `db.Apis` under the tenant-scoped `CatalogDbContext` (`LoadAndAuthorizeApiAsync`, `CatalogEndpointDelegates.cs:918-925`) inside the transport-managed `ITenantScope`; the added `IOptions` param is stateless. No new DB access path.
- **Domain-invariant relaxation safety** (Decision 12): the byte-cap check dropped from `ApiSpec.Validate` is re-established at the sole write path. `ApiSpec.Create` has one production caller (`UpsertApiSpecHandler.cs:25`) and `.Replace` one (same handler), and the handler is invoked only from `UpsertApiSpecAsync:685` — no Kafka consumer / import path. That endpoint gates size via `ReadCappedAsync(reader, maxBytes, ct)` before the handler runs, so nothing can persist an oversized spec. Verified by grep across `Kartova.Catalog` (not codelens — const/instance-call blast radius).
- **ADR-0112 amendment soundness:** the validator ceiling (50 MiB) bounds the `StringBuilder` that `ReadCappedAsync` fills, so the "configurable" cap cannot become an unbounded-memory vector; `int` holds 50 MiB; `long? ContentLength > int maxBytes` widens correctly. The store-as-text vs MinIO decision is untouched. Amendment text (ADR-0112:15) matches the implementation.

### Should-fix issues

- **Streamed (chunked / no-`Content-Length`) enforcement of the *configured* cap is untested.**
  **Evidence:** `Kartova.Catalog.IntegrationTests/ApiSpecTests.cs:236-255` (`PUT_over_configured_cap_returns_400_naming_the_limit`). The `over` body is sent via `SpecRequest` → `StringContent`, which sets `Content-Length`, so the request is rejected by the **declared-length pre-check** (`CatalogEndpointDelegates.cs:669-671`) and never reaches `ReadCappedAsync`. The old `PUT_with_oversized_content_returns_400` (which exercised a large body) was *replaced*, not supplemented, so no integration test now drives the streamed cap at all — and none ever drove it at the *configured* value.
  **Impact:** the `ReadCappedAsync(reader, maxBytes)` line (the defense against a chunked body bypassing the declared-length check — the explicitly-commented threat) has no real-seam coverage reading the configured value. A regression that reverted it to a constant, or broke the streamed accumulation, would pass this suite. Gate 6 (mutation, blocking here) is the intended backstop but the ledger shows it still RUNNING.
  **Fix:** add one integration case that PUTs a body over the configured small cap with `Transfer-Encoding: chunked` (or a `StreamContent` over a non-seekable stream so no `Content-Length` is set) and asserts 400 + the cap value in `detail`. Same fixture/`ConfigureTestServices` override already in the file.

- **DoD not yet green — completion cannot be claimed on this HEAD.**
  **Evidence:** `docs/superpowers/verification/2026-07-07-catalog-api-spec-ui/dod.md` summary table — gate 5 (`/simplify`), gate 6 (mutation, **blocking** this slice per plan Global Constraints), gate 8 (`review-pr`), gate 9, Playwright, terminal re-verify, and `ci-local.sh` are all PENDING/RUNNING at `509fa0e`.
  **Impact:** per CLAUDE.md the honest status is "implementation staged, verification pending," not "slice complete." Gate 6 being blocking (Domain/Application touched) means it must land green with survivors documented before merge.
  **Fix:** finish gate 6 and record its score/survivors; run gates 5/8, Playwright (ADR-0084 — the plan itself flags the react-aria blank-page-on-dialog-open risk), and the terminal re-verify + `ci-local.sh`; update the ledger. (Inherent to this being gate 9 — flagged so it isn't lost.)

### Nits

- **`ApiSpecSection` renders an empty gap when `hasSpec` is true but the GET resolves to `null`.**
  **Evidence:** `ApiSpecSection.tsx` (diff 2198-2213) — the `hasSpec` branch renders `spec.data && (...)`, `spec.isLoading && ...`, `spec.isError && ...`, but nothing when `data === null && !isLoading && !isError`. `useApiSpec` returns `null` on 404 (`apis.ts` queryFn). If `api.hasSpec` is stale-true while the backend has no spec (drift / eventual invalidation race), the section shows only the header + Replace button with a blank body.
  **Impact:** cosmetic empty state in a rare drift window; not a failure.
  **Fix:** add a `{hasSpec && !spec.data && !spec.isLoading && !spec.isError && <p className="text-sm text-tertiary italic">No spec attached.</p>}` fallback (or fold into the empty-state branch).

- **`<pre className="… break-all">` breaks long unbroken tokens mid-character.**
  **Evidence:** `ApiSpecSection.tsx` diff 2206. For a minified single-line JSON spec, `break-all` wraps at arbitrary characters rather than whitespace, hurting readability.
  **Impact:** purely visual.
  **Fix:** prefer `break-words` (or `overflow-x-auto` + `whitespace-pre` for horizontal scroll of the raw document).

### Missing tests

- **Streamed/chunked configured-cap rejection** — see Should-fix #1. Test: `Kartova.Catalog.IntegrationTests/ApiSpecTests`, chunked body over a small configured cap ⇒ 400 with cap in `detail`; a body under the cap over the same transport ⇒ 201.
- **`ApisTable` skeleton cell count** — the plan changed `cells={6}`→`7` (diff 2264) but no test asserts the loading skeleton column count; only the populated-row column is covered (`ApisTable.test.tsx` diff 2539-2549). Test: render with `isLoading: true`, assert 7 skeleton cells / header includes "Spec".
- **`inferMediaType` XML / no-extension-no-brace edge** — covered cases are `.yaml/.yml/.json` + `{`-sniff + non-brace→yaml (`AttachApiSpecDialog.test.tsx` diff 2582-2589). A `.xml`/`.txt` filename falls through to content-sniff (returns json/yaml, never rejected client-side) — acceptable per Decision 7 (server is authoritative) but worth one assertion documenting the fall-through so it isn't mistaken for a bug.
- **`ApiSpecSection` loading state** — `spec.isLoading` branch (diff 2211) has no test (the mock hard-codes `isLoading: false`). Low value; optional.

### What looks good

- **Domain/transport boundary is drawn correctly.** Size is an operational/transport bound enforced at the streaming seam, not a domain invariant (Decision 12) — `ApiSpec.Validate` keeps exactly the invariants that are truly domain (non-empty + allowed media type, `ApiSpec.cs:53-59`). Removing the const-in-domain that duplicated config is the right way to kill the drift risk.
- **Defense-in-depth on the cap survives the refactor.** Both the cheap declared-length pre-check *and* the independent streamed `ReadCappedAsync` now read the same `maxBytes` (`CatalogEndpointDelegates.cs:661,669-677`), with the chunked-bypass rationale preserved in the comment — the refactor didn't weaken the two-layer guard.
- **`ValidateOnStart` + a real band, not just `Bind`.** `CatalogSpecOptionsValidator` (1 KiB…50 MiB) fails fast at boot on an operator typo, and the failure message names the section + offending value — a genuinely fail-safe options wiring, not a rubber-stamp.
- **The raw-`fetch` escape hatch is scoped and justified.** `useApiSpec`/`useUpsertApiSpec` bypass openapi-fetch only for the two endpoints that need byte-exact YAML bodies + verbatim `Content-Type`, mirroring the established `organization.ts` logo-upload precedent, and still route errors through `throwWithStatus` for a consistent ProblemDetails shape. GET's `Results.Text(content, mediaType)` on the server makes the media-type round-trip coherent.
- **ADR-0107 field-addition trigger handled explicitly.** The new `hasSpec` field gets a recorded three-axis decision (column ✓ / sort ✗ / filter deferred) in `list-filter-registry.md`, and the column is a plain `Table.Head` not `SortableHead` — matching the design intent that presence isn't a meaningful sort key.
