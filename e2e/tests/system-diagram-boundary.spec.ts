import { test, expect, type Page, type APIRequestContext } from "@playwright/test";
import { login } from "../fixtures/auth";

/**
 * Regression spec for the System diagram's per-node membership marking (spec §3.1 amendments
 * 7a/4a). The real contract, established after gate 9 disproved decision 7, is per-node: every
 * non-member carries the "outside this system" marking and no member does — membership is NOT,
 * and structurally cannot be, read off whether a node's rect intersects the boundary band. §3.1
 * explains why: with dagre's `rankdir: "LR"` layout and `partOf` edges retained as layout input, a
 * member's *external* dependency can share a rank — and therefore an x-column — with a member, so
 * an "no non-member intersects the band" assertion would be geometry-fragile (it held on the
 * original fixture by 12px of luck) and could redden the nightly on a legibility-only
 * `BOUNDARY_PADDING` retune with no defect present. This spec instead pins the two per-node
 * marking assertions (the real contract) plus the one band-geometry property 4a actually
 * guarantees deterministically: every *member* intersects the band. No unit test can catch a
 * regression here — the repo's `@xyflow/react` mock (see SystemDiagram.test.tsx) renders labels
 * only, never real DOM geometry, so this has to be driven on the real stack.
 *
 * Seeds its own fixture through the product's own API, using the session `login()` already
 * established — no environment variable, no assumption about what the shared dev tenant already
 * contains. Builds: a System, two member Services (`PUT /catalog/services/{id}/system`), a
 * `dependsOn` edge between the two members, and a `dependsOn` edge from each member to a distinct
 * non-member Service. That is the minimum shape that exercises both assertions below: an
 * inter-member edge (proves membership classification ran) and a member -> non-member edge (the
 * one the boundary-band bug needs).
 *
 * Catalog has no delete endpoint for Systems/Services (audit-log-first domain, no hard delete —
 * consistent with every other list/detail slice), so each run's System and Services are named with
 * a run-scoped suffix and left in the shared dev tenant, same as the integration-test seed helpers
 * this spec mirrors (`GetCatalogGraphTests.cs`).
 *
 * The Team, however, is find-or-created against a fixed name and reused across runs (spec S10):
 * a run-scoped team name here would add ~two teams a night against the `limit: 200` team lookups
 * `SystemDetailPage`/`GraphExplorerPage` make — a slow-fuse failure that would only surface months
 * later, once the shared tenant's team count crosses 200, as team names silently failing to
 * resolve. Moving the whole fixture into `DevSeed` is the fuller fix, recorded as a follow-up.
 */
const API_BASE_URL = process.env.E2E_API_BASE_URL ?? "http://localhost:8080";
const RUN_ID = Date.now().toString(36);

const MEMBER_A = `Boundary Band Member A ${RUN_ID}`;
const MEMBER_B = `Boundary Band Member B ${RUN_ID}`;
const EXTERNAL_A = `Boundary Band External A ${RUN_ID}`;
const EXTERNAL_B = `Boundary Band External B ${RUN_ID}`;
const MEMBERS = [MEMBER_A, MEMBER_B];
const EXTERNALS = [EXTERNAL_A, EXTERNAL_B];

/** Pull the access token react-oidc-context stashed in sessionStorage (tab-scoped, per
 *  authConfig.ts) after `login()` completes — the same token `apiClient`'s auth middleware
 *  attaches to every SPA request, reused here to hit the API directly and seed a fixture. */
async function accessToken(page: Page): Promise<string> {
  const token = await page.evaluate(() => {
    for (let i = 0; i < sessionStorage.length; i++) {
      const key = sessionStorage.key(i);
      if (key?.startsWith("oidc.user:")) {
        const raw = sessionStorage.getItem(key);
        if (!raw) return null;
        return (JSON.parse(raw) as { access_token?: string }).access_token ?? null;
      }
    }
    return null;
  });
  if (!token) throw new Error("no oidc.user session entry found after login() — cannot seed the fixture via the API");
  return token;
}

async function apiPost<T>(
  request: APIRequestContext,
  token: string,
  path: string,
  data: unknown,
): Promise<T> {
  const resp = await request.post(`${API_BASE_URL}${path}`, {
    headers: { Authorization: `Bearer ${token}` },
    data,
  });
  expect(resp.ok(), `POST ${path} -> ${resp.status()}: ${await resp.text()}`).toBeTruthy();
  return resp.json() as Promise<T>;
}

async function apiPut(request: APIRequestContext, token: string, path: string, data: unknown): Promise<void> {
  const resp = await request.put(`${API_BASE_URL}${path}`, {
    headers: { Authorization: `Bearer ${token}` },
    data,
  });
  expect(resp.ok(), `PUT ${path} -> ${resp.status()}: ${await resp.text()}`).toBeTruthy();
}

async function apiGet<T>(request: APIRequestContext, token: string, path: string): Promise<T> {
  const resp = await request.get(`${API_BASE_URL}${path}`, { headers: { Authorization: `Bearer ${token}` } });
  expect(resp.ok(), `GET ${path} -> ${resp.status()}: ${await resp.text()}`).toBeTruthy();
  return resp.json() as Promise<T>;
}

const FIXTURE_TEAM_NAME = "Boundary Band E2E Fixture Team";

/**
 * Find-or-create against a fixed name so the nightly run reuses one team instead of minting a new
 * one every run (spec S10) — see the file header for why an unbounded team count is a slow-fuse bug.
 */
async function findOrCreateFixtureTeam(request: APIRequestContext, token: string): Promise<{ id: string }> {
  const page = await apiGet<{ items: { id: string; displayName: string }[] }>(
    request,
    token,
    `/api/v1/organizations/teams?displayNameContains=${encodeURIComponent(FIXTURE_TEAM_NAME)}&limit=50`,
  );
  const existing = page.items.find((t) => t.displayName === FIXTURE_TEAM_NAME);
  if (existing) return existing;
  return apiPost(request, token, "/api/v1/organizations/teams", {
    displayName: FIXTURE_TEAM_NAME,
    description: "system-diagram-boundary.spec.ts fixture — reused across runs, never re-created.",
  });
}

async function dependsOn(request: APIRequestContext, token: string, sourceId: string, targetId: string) {
  await apiPost(request, token, "/api/v1/catalog/relationships", {
    sourceKind: "service",
    sourceId,
    type: "dependsOn",
    targetKind: "service",
    targetId,
  });
}

test("every member intersects the boundary band and carries no outside marking, while every non-member does", async ({
  page,
  request,
}) => {
  const consoleErrors: string[] = [];
  page.on("console", (m) => {
    if (m.type() === "error") consoleErrors.push(m.text());
  });
  page.on("pageerror", (e) => consoleErrors.push(String(e)));

  await login(page);
  const token = await accessToken(page);

  // --- seed the fixture through the product's own API -----------------------------------------
  const team = await findOrCreateFixtureTeam(request, token);

  const system = await apiPost<{ id: string }>(request, token, "/api/v1/catalog/systems", {
    displayName: `Boundary Band E2E System ${RUN_ID}`,
    description: "system-diagram-boundary.spec.ts fixture",
    teamId: team.id,
  });

  const registerService = (displayName: string) =>
    apiPost<{ id: string }>(request, token, "/api/v1/catalog/services", {
      displayName,
      description: "system-diagram-boundary.spec.ts fixture",
      teamId: team.id,
      endpoints: [],
    });

  const memberA = await registerService(MEMBER_A);
  const memberB = await registerService(MEMBER_B);
  const externalA = await registerService(EXTERNAL_A);
  const externalB = await registerService(EXTERNAL_B);

  await apiPut(request, token, `/api/v1/catalog/services/${memberA.id}/system`, { systemId: system.id });
  await apiPut(request, token, `/api/v1/catalog/services/${memberB.id}/system`, { systemId: system.id });

  await dependsOn(request, token, memberA.id, memberB.id); // between members
  await dependsOn(request, token, memberA.id, externalA.id); // member -> non-member
  await dependsOn(request, token, memberB.id, externalB.id); // member -> non-member (second, distinct)

  // --- drive the diagram ------------------------------------------------------------------------
  await page.goto(`/catalog/systems/${system.id}?tab=members`);
  await expect(page.getByRole("region", { name: /system diagram/i })).toBeVisible();
  // Waiting for the band proves layout ran and members were classified before we measure anything.
  await expect(page.locator(".react-flow__node-systemBoundary")).toBeVisible();

  // NOTE: a plain .click() on the switch itself fails Playwright's actionability check — the
  // control is a react-aria `Switch` (a `<label>` wrapping a visually-hidden `role="switch"`
  // input), so clicking the input's own (near-zero) box resolves to whatever sits on top of it.
  // A real mouse user clicks the visible label text, which the browser's native label->input
  // delegation forwards to the input — verified here rather than assumed (spec S7).
  const toggle = page.getByRole("switch", { name: /include external dependencies/i });
  await page.getByText(/include external dependencies/i).click();
  await expect(toggle).toBeChecked();
  // The external nodes must be on the canvas before geometry is measured.
  await expect(page.getByText(EXTERNAL_A, { exact: true })).toBeVisible();
  await expect(page.getByText(EXTERNAL_B, { exact: true })).toBeVisible();

  const geometry = await page.evaluate(() => {
    const band = document.querySelector<HTMLElement>(".react-flow__node-systemBoundary");
    const bandRect = band?.getBoundingClientRect();
    const nodes = [...document.querySelectorAll<HTMLElement>(".react-flow__node-entity")].map((n) => {
      const r = n.getBoundingClientRect();
      const card = n.querySelector<HTMLElement>("div");
      return {
        // A node's text content is displayName + kind label concatenated (e.g.
        // "Boundary Band External A ...Service"), never the display name alone.
        label: (n.querySelector("div > div")?.textContent ?? "").trim(),
        left: Math.round(r.left),
        top: Math.round(r.top),
        right: Math.round(r.right),
        bottom: Math.round(r.bottom),
        // spec §3 row 7a: the outside-this-system state is a dashed border on the node card.
        dashed: (card?.className ?? "").includes("border-dashed"),
      };
    });
    return {
      band: bandRect
        ? {
            left: Math.round(bandRect.left),
            top: Math.round(bandRect.top),
            right: Math.round(bandRect.right),
            bottom: Math.round(bandRect.bottom),
          }
        : null,
      nodes,
    };
  });

  const band = geometry.band;
  expect(band, "the boundary band must be present once members are classified").not.toBeNull();
  const intersects = (n: { left: number; top: number; right: number; bottom: number }) =>
    n.left < band!.right && n.right > band!.left && n.top < band!.bottom && n.bottom > band!.top;
  // NOTE the matcher: see the label comment above — an exact-equality match against the display
  // name alone would silently match nothing and make every check below vacuously pass. Substring,
  // deliberately, with the cardinality guards immediately below so a matcher that matches nothing
  // fails loudly instead of reporting a clean (meaningless) result — this is exactly how the
  // gate-9 probe this spec replaces first reported "clean" while the defect was on screen.
  const named = (labels: string[]) => (n: { label: string }) => labels.some((l) => n.label.startsWith(l));

  const externalNodes = geometry.nodes.filter(named(EXTERNALS));
  const memberNodes = geometry.nodes.filter(named(MEMBERS));
  expect(externalNodes, "both non-member services must be on the canvas").toHaveLength(2);
  expect(memberNodes, "both member services must be on the canvas").toHaveLength(2);

  // The deterministic geometric property 4a actually guarantees: the band is a bounding box over
  // the member nodes (spec §3.1), so every member must intersect it. The converse — no non-member
  // intersects the band — is NOT guaranteed (§3.1) and must not be asserted here: it held on the
  // original fixture by 12px of luck, and `BOUNDARY_PADDING` (systemBoundary.ts) is documented as
  // safe to retune for legibility alone, which would flip this property with no defect present.
  const membersOutsideBand = memberNodes.filter((n) => !intersects(n));
  expect(
    membersOutsideBand,
    `member(s) rendered outside the boundary band: ${JSON.stringify(membersOutsideBand)}`,
  ).toHaveLength(0);

  expect(externalNodes.every((n) => n.dashed), "every non-member must carry the outside-boundary marking").toBe(
    true,
  );
  expect(memberNodes.some((n) => n.dashed), "no member may carry the outside-boundary marking").toBe(false);

  expect(consoleErrors, `console errors: ${consoleErrors.join(" | ")}`).toEqual([]);
});
