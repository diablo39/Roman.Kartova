# PR review (DoD gate 8, pr-review-toolkit lenses) — Catalog API spec UI + configurable cap

**Range:** `a07f59a..8a5a2dc` · **Reviewed:** 2026-07-07 · **Reviewer:** gate-8 independent pass
**Scope:** NEW issues under five lenses (silent-failure, type-design, test-analysis, comment-accuracy, general/CLAUDE.md). Prior-review fixes and accepted items excluded per brief.

## Verdict

**Ready to merge? — Yes, with fixes.**
No Critical. One Important (a test-coverage gap that lets a real regression pass green). The rest are Minor/nit and can land as follow-ups without blocking.

| Severity | Count |
|---|---|
| Critical | 0 |
| Important | 1 |
| Minor | 5 |
| Nit | 2 |

---

## Important

### I-1 · Configured *streaming* cap (`ReadCappedAsync`) has no test — only the declared-length pre-check is covered
**Lens:** pr-test-analyzer
**Where:** `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/ApiSpecTests.cs` (`PUT_over_configured_cap_returns_400_naming_the_limit`, diff :1828) vs `CatalogEndpointDelegates.cs:1638` (declared-length) and `:1647` (`ReadCappedAsync(reader, maxBytes, ct)`).

`UpsertApiSpecAsync` enforces the cap on **two** independent paths:
1. Declared `Content-Length > maxBytes` → `SpecTooLarge` (early-out).
2. Streamed `ReadCappedAsync(reader, maxBytes)` → `null` → `SpecTooLarge` — the path whose comment claims *"a chunked-transfer request (no Content-Length) cannot bypass the limit."*

The new test sends a `string` body via `HttpClient`, which sets `Content-Length`, so it exercises **only path 1**. Path 2 at the *configured* value is asserted by a code comment, not a test. A regression that left `ReadCappedAsync` reading a stale/default limit (or dropped its `null` return) would keep the whole suite green. Given Task 2 rewired exactly this call and gate 6 (mutation) is blocking here, this is the one coverage hole worth closing: add a chunked-transfer (no `Content-Length`) case against the overridden small cap, asserting 400. (Mutation-sentinel on `UpsertApiSpecAsync`/`ReadCappedAsync` may surface the same survivor — reconcile there.)

---

## Minor

### M-1 · `CopyButton` reports success even when the clipboard write fails
**Lens:** silent-failure-hunter · **Where:** `web/src/features/catalog/components/ApiSpecSection.tsx:2237` (`void navigator.clipboard.writeText(text); setCopied(true);`)

The write promise is voided and `setCopied(true)` fires unconditionally, so a rejected `writeText` (permission denied, blocked by policy) still shows "Copied" — the UI asserts a success that didn't happen. Additionally, on a non-secure origin `navigator.clipboard` is `undefined`, so `.writeText` throws a `TypeError` synchronously inside the click handler (unhandled). Prod is HTTPS and dev is localhost (both secure), so impact is low, but the false-positive "Copied" is a genuine silent failure. Gate `writeText` on success (`.then(() => setCopied(true)).catch(...)`) and guard `navigator.clipboard`.

### M-2 · `onFile` swallows a `file.text()` rejection with no feedback
**Lens:** silent-failure-hunter · **Where:** `AttachApiSpecDialog.tsx:2371` (`const text = await file.text();`) invoked as `void onFile(...)` at :2411.

If `file.text()` rejects (unreadable file, revoked permission), the rejection is discarded by `void` and the user sees nothing — no content loads, no error. Rare, but a `try/catch` setting the existing `error` state would close the gap.

### M-3 · Hook-level error branches are untested
**Lens:** pr-test-analyzer · **Where:** `apis.ts` `useUpsertApiSpec` non-ok branch (:2141 JSON-parse-fallback) and `useApiSpec` non-404 `!res.ok` branch (:2113).

`apis.test.tsx` covers the happy GET, 404→null, disabled, and the happy PUT call shape — but never a non-ok response. The `try { res.json() } catch` fallback and the `throwWithStatus` composition (both touched/added this slice) have no direct coverage. The dialog test exercises rejection surfacing with the mutation **mocked**, so the hook's own error logic is still unverified.

### M-4 · `useUpsertApiSpec` `onSuccess` invalidation untested
**Lens:** pr-test-analyzer · **Where:** `apis.ts:2155` (invalidates `detail` / `spec` / `all`).

This three-key invalidation is what makes `hasSpec`, the detail view, and the list Spec column refresh after an upload — the core UX contract of the slice — yet no test asserts it. A wrong/missing key would silently leave the UI stale.

### M-5 · File-upload path of `AttachApiSpecDialog` not exercised
**Lens:** pr-test-analyzer · **Where:** `AttachApiSpecDialog.test.tsx` — covers `inferMediaType` (unit) + paste + empty + 415 + Replace title + override persistence, but not the `<input type=file>` → `onFile` → content/mediaType population path, which the spec named as a deliverable ("file path + paste path"). Consider a `userEvent.upload` case.

---

## Nits

### N-1 · Inline FQN instead of a `using`
`CatalogEndpointDelegates.cs:1613` — `Microsoft.Extensions.Options.IOptions<CatalogSpecOptions>` written fully-qualified in the signature; the file has no `using Microsoft.Extensions.Options;`. Cosmetic; a using would match house style.

### N-2 · `<pre>` uses `break-all`
`ApiSpecSection.tsx:2206` — `break-all` breaks long tokens/URLs mid-character in the spec view, hurting readability of an OpenAPI/AsyncAPI doc. `break-words` (or `whitespace-pre` + horizontal scroll) reads better for code-like content.

---

## Lens notes (clean / accurate)

- **type-design-analyzer:** `CatalogSpecOptions` (mutable `int` setter, invariant enforced out-of-band by `CatalogSpecOptionsValidator` + `ValidateOnStart`) is the idiomatic options pattern — acceptable; the safe-band invariant is expressed by the validator, not the type, which is correct here. `int` is sufficient for a 50 MiB ceiling; the `long? ContentLength > int maxBytes` comparison promotes cleanly. TS `MediaType` union properly constrains the two allowed values. No blocking type-design issues.
- **comment-analyzer:** All touched comments are accurate — `ApiSpec` class-doc size note, `SpecTooLarge` doc, `ProblemTypes.cs:85`, and `CatalogSpecOptions`/validator docs correctly describe the new configurable-cap behavior and the OOM-bound rationale. The one comment that outruns its evidence is the `ReadCappedAsync` "cannot bypass the limit" claim (see I-1) — accurate as a description, just untested.
- **general/CLAUDE.md:** Domain byte-cap removal + endpoint enforcement matches ADR-0112 amendment; no new `KartovaPermission` (5-sync correctly not triggered); enum wire values camelCase; `ApisTable` header/skeleton/body column counts stay consistent (6→7); `isRowHeader` on `displayName` preserved (ADR-0084 guard, with a test). Note (non-blocking): the committed DoD ledger in-range shows gates 5/6/8/9 PENDING/RUNNING — expected, since this review *is* gate 8; update the ledger + `gate-findings.yaml` after.

## Pre-existing (not introduced here, non-blocking)

- The declared-length pre-check runs **before** `LoadAndAuthorizeApiAsync`, so a non-member authenticated caller can receive a 400 "too large" (leaking the configured cap) ahead of the 403. Ordering predates this slice (unchanged by the diff); flagged only because the function was touched.
