# Gate-10 Visual/API Verification — Graph Impact Analysis

**Branch:** `feat/catalog-graph-impact-analysis`
**Date:** 2026-07-10
**ADR reference:** ADR-0084 (cold-start dev server, in-SPA navigation, Playwright MCP verification)
**Evidence folder:** `docs/superpowers/verification/2026-07-10-catalog-graph-impact-analysis/`

## Environment

- Docker compose stack confirmed running via `docker compose ps` (api, keycloak, keycloak-db, postgres, elasticsearch, kafka, etc.) — left running throughout and after this verification, per instruction.
- API: `http://localhost:8080` (from `docker-compose.yml`: `Authentication__Authority=http://localhost:8180/realms/kartova`, `Authentication__Audience=kartova-api`).
- KeyCloak: `http://localhost:8180`, realm `kartova`. Clients (from `deploy/keycloak/kartova-realm.json`):
  - `kartova-web` — public, `directAccessGrantsEnabled=false` (SPA, PKCE/redirect only).
  - `kartova-api` — public, `directAccessGrantsEnabled=true` — used for scripted password-grant token acquisition.
  - `kartova-admin` — confidential, service-account only (not used here).
- Web dev env (`web/.env.development`): `VITE_API_BASE_URL=http://localhost:8080`, `VITE_OIDC_AUTHORITY=http://localhost:8180/realms/kartova`, `VITE_OIDC_CLIENT_ID=kartova-web`.
- Vite dev server cold-started with `cd web && cmd //c "npm run dev"` (no `npm ci`) in the background. Log (`vite.log`, scratchpad) confirmed: predev codegen fetched live OpenAPI from `http://localhost:8080/openapi/v1.json`, then `VITE v6.4.2 ready in 2057ms`, listening on `http://localhost:5173/`. Verified independently via `curl http://localhost:5173/` → `HTTP:200` (the background-task "completed (exit code 0)" notification was a harness artifact, not an actual server exit — server was confirmed live).

## Seed scenario (live API, real auth)

Token acquired via OIDC password grant against `kartova-api` client for `admin@orga.kartova.local` / `dev_password_12`. Seeded via `POST /api/v1/catalog/services` and `POST /api/v1/catalog/relationships` with the bearer token, matching the request/response shapes confirmed in `RegisterServiceTests.cs` and `CreateRelationshipTests.cs`, and the tier semantics confirmed in `GetImpactAnalysisTests.cs` (`dependsOn`: source = dependent, target = dependency; impact depth = hops of reverse `dependsOn` reachability from focus):

- Team: seeded under org A tenant.
- Services: `impact-focus`, `impact-a`, `impact-b`.
- Relationships: `impact-a --dependsOn--> impact-focus` (tier 1), `impact-b --dependsOn--> impact-a` (tier 2).

Focus entity ID: `213d9e2b-48f0-4c33-bcbf-153806aa9301` (`impact-focus`).

## In-browser verification (Playwright MCP)

1. Navigated to `http://localhost:5173/graph?focus=service:213d9e2b-48f0-4c33-bcbf-153806aa9301` — unauthenticated, redirected to KeyCloak login as expected.
2. Logged in as `admin@orga.kartova.local` / `dev_password_12` via the real OIDC redirect flow (filled KeyCloak form, submitted, redirected back to the app). App correctly returned to the original deep-linked route with the `focus` query param preserved.
3. Graph rendered: `Dependency graph` view, "Node details" sidebar for `impact-focus` showed `depth 0 from focus`, and an "Impact analysis" action button.
4. Clicked the focus node → sidebar confirmed selection → clicked **Impact analysis**.
   - Banner text: **"2 downstream (1× tier-1, 1× tier-2)"**, with a **Close Analysis** button.
   - `impact-a` node rendered with a red tier-1 ring; `impact-b` node rendered with an amber/gold tier-2 ring.
   - Screenshot saved: `docs/superpowers/verification/2026-07-10-catalog-graph-impact-analysis/impact-active.png`.
   - **Caveat:** at the pan/zoom position captured, the focus node (`impact-focus`) itself was outside the screenshot's visible viewport. Its "lit"/selected depth-0 state was independently confirmed via the accessibility snapshot (sidebar actively showing `impact-focus`, `depth 0 from focus`), just not co-visible in this particular frame.
   - **Caveat:** the seeded scenario (as specified) contains exactly 3 nodes — `impact-focus` + the two downstream tiers — so there were no "unrelated" nodes present in the graph to visually confirm dimming of non-impacted nodes. This is a gap in what the given seed data can demonstrate, not an execution error.
5. Clicked **Close Analysis** → banner and tier-ring styling disappeared; graph and sidebar returned to the normal (pre-analysis) state (`Reset to focus` link, plain node cards, no banner).
   - Screenshot saved: `docs/superpowers/verification/2026-07-10-catalog-graph-impact-analysis/impact-closed.png`.
6. Console messages checked at multiple checkpoints (initial load, after opening analysis, after closing analysis, and a final full-session check with `all: true`): consistently **0 errors, 0 warnings**, 3 total messages across the whole session (login, navigation, node interactions, toggle, close). No runtime errors observed at any point.

Non-blocking issue: a secondary "Fit View" control click (not part of the required steps) timed out because the graph legend overlapped it at that screen coordinate. Not retried/forced since primary evidence was already captured by other means; did not block any required step.

## Live-API verification

`GET /api/v1/catalog/impact?entityKind=service&entityId=213d9e2b-48f0-4c33-bcbf-153806aa9301` with bearer token → **200 OK**. Response saved to `docs/superpowers/verification/2026-07-10-catalog-graph-impact-analysis/impact-api-response.json`:

- `nodes`: `impact-focus` depth 0, `impact-a` depth 1, `impact-b` depth 2.
- `edges`: explicit `impact-a → impact-focus` (dependsOn), `impact-b → impact-a` (dependsOn), both `origin: "manual"`.
- `derivedEdges`: empty (none expected — scenario used only explicit relationships).
- `truncated`: `false`.

Matches the expected shape/semantics from `GetImpactAnalysisTests.Multi_tier_blast_radius_includes_explicit_and_derived`.

## Verified vs Pending

**VERIFIED**

- Docker compose stack confirmed running; API/KeyCloak endpoints and clients correctly identified.
- Vite dev server cold-started (no `npm ci`) and confirmed live on port 5173.
- Real OIDC login flow (redirect → KeyCloak → back to deep-linked route with `focus` param preserved) works end-to-end.
- Seed scenario created via live API with real password-grant auth: 3 services, 2 `dependsOn` relationships, correct tiers.
- Graph renders the focus entity's dependency graph without errors.
- Impact analysis activation: banner text "2 downstream (1× tier-1, 1× tier-2)" correct; tier-1 (red) / tier-2 (amber) node ring styling correct.
- Close Analysis restores the pre-analysis view (banner/ring styling removed).
- Zero console errors/warnings across the entire session (checked at 4 points).
- Live API `GET /api/v1/catalog/impact` returns 200 with correct depth-0/1/2 nodes, correct explicit edges, empty derived edges, `truncated:false`.

**PENDING (with reason)**

- **Dimming of unrelated/non-impacted nodes** — not verifiable with the given seed data: the specified 3-node scenario has no entities outside the blast radius, so there is nothing in this graph that should visually dim. Would require an additional unrelated service (outside the task's specified seed set) to demonstrate.
- **Co-visibility of focus node "lit" state in the same screenshot frame as the tier-ringed nodes** — the focus node's active/selected state was confirmed via the accessibility snapshot/sidebar rather than being in-frame in `impact-active.png` due to the canvas pan/zoom position at capture time. Functionally confirmed, but not in a single screenshot.
- **"Fit View" control interaction** — not part of the required steps; attempted opportunistically, blocked by an overlapping legend element at that click coordinate, not retried since it wasn't required evidence.

## Cleanup

- Background Vite dev server process (serving port 5173) stopped.
- Docker compose stack left running, per instruction.
