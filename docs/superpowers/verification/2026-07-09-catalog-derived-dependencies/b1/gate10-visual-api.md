# Gate 10 — Visual / API verification (observe running system)

## API side — DONE (real seam + live stack shape)
- **Real-seam coverage (gate 3):** 14 integration tests in `GetCatalogGraphTests` exercise the live `/catalog/graph` endpoint through **real JWT auth + real Postgres/RLS** (Testcontainers) — derived-edge presence + provenance (api + via-app names), derived-edge **drives discovery** (outbound AND inbound directional focus), **explicit-wins** suppression, **cross-tenant isolation** (both tenants seed complete derived topologies; A sees only its own edge, zero B-id leakage). This is the endpoint exercised with real auth + DB.
- **Running compose stack confirmed:** the live API (`:8080`) OpenAPI advertises `GraphResponse.derivedEdges` + `DerivedEdgeDto`/`DerivationPathDto` (codegen fetched it live during Task 4). Endpoint enforces auth (unauth GET → HTTP 401).
- A full **authenticated seeded drive on the compose stack** was not scripted: the public SPA client `kartova-web` has direct-access grants (ROPC) **disabled by design** (`unauthorized_client: Client not allowed for direct access grants`), so a token requires the interactive OIDC auth-code flow. No additional data-shape risk beyond the 14 real-seam tests.

## Visual side — PENDING USER VERIFICATION
- The dashed derived-edge styling + legend + compact provenance label are **unit-covered** (`graphMerge.test.ts` / `graphLayout.test.ts`, 12/12 — dashed style asserted, dim+dashed composition asserted, dedup across results asserted).
- A real-browser screenshot (ADR-0084) of `/graph` showing the dashed derived edge + legend was **not captured**: **Playwright MCP is not connected in this session**. Per CLAUDE.md ("UI changes you can't verify in a browser: say so"), this is flagged **pending user verification**.
- **Follow-up:** the deterministic user-flow regression (derived edge renders dashed in the explorer) belongs in the nightly Playwright E2E suite (`e2e/`) — the expected home for gate-10 findings that become regressions.
