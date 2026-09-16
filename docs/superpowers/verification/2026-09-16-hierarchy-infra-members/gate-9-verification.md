# Gate 9 — Visual / running-system verification

**Date:** 2026-09-16 · **Commit:** 69064cd · **Stack:** local `docker compose` (postgres 18, keycloak 26, api, web) cold-started from freshly built images. Web at http://localhost:4173, OIDC via Keycloak. Logged in as `admin@orga.kartova.local` (Org A / OrgAdmin).

## TD-006 — infrastructure member in the System Members table

System **Payments Platform** (`77b78e53-…`) → **Members** tab. The pre-existing infra member **`g9-7543-n0`** (Kind = *Infrastructure*) renders as:
- a **link** to `/catalog/infrastructure/vms/94b4ac26-…` — the **correct nested VM route** (proves the gate-6 fix; before it, `entityDetailPath` produced the dead `/catalog/infrastructure/{id}`),
- with a working **Remove** button.

**Click-through verified:** clicking the member link navigated to the VM detail page (heading "g9-7543-n0", "Virtual machine", "Running", Edit/Delete) — **not** the "Not found" fallback. This is the exact defect gate 6 caught, now confirmed fixed in a real browser.

Evidence: `gate9-system-members-infra.png`.

## TD-005 — infrastructure in the catalog hierarchy

Registered a VM **`0000-gate9-hier-vm`** (Demo Team; `0000-` prefix so it sorts within the hierarchy's 200-node cap) and assigned it to **Payments Platform** via the VM detail → *Assign* system dialog.

Catalog hierarchy (`/catalog/hierarchy`) → Org A → Demo Team → **Payments Platform (System, 4)** now lists:
- **`0000-gate9-hier-vm` — Infrastructure**, linking to `/catalog/infrastructure/vms/f911c4de-…`,
- alongside Checkout Service / Fees / Ledger.

Team "Ungrouped" bucket dropped **95 → 94** as the VM moved into the system — confirming the read-model routing (assigned infra → under its system; unassigned infra → owning-team Ungrouped).

Evidence: `gate9-hierarchy-infra-member.png`.

Note on the large seed: the hierarchy is node-capped at 200 and Org A has 200+ components; a lowercase-named VM (e.g. the fixture `g9-7543-n0`) sorts past the cap and is truncated from the tree (pre-existing `nodeCap` behavior, ADR-unrelated to this slice) — hence the `0000-`-prefixed VM to land it within the cap for a clean screenshot.

## Console

No application errors. Only a cosmetic `GET /vite.svg 404` (missing favicon in the E2E image), unrelated to the slice.

## Out-of-scope finding (new follow-up, NOT fixed in this slice)

The System-side **"Assign component"** dialog (`AddSystemMemberDialog`) offers only **Application / Service** radios — it cannot assign an **Infrastructure** member from the System side. Infra membership is only settable from the VM detail → *Assign* dialog (which works). This is a UX-parity gap beyond TD-005/006's render scope (both TDs were about surfacing infra that is already assigned). Recommend filing as a new tech-debt item (sibling to the closed TD-006). Not addressed here to keep the slice scoped.

## Verdict

Gate 9 **PASS**. Both acceptance criteria verified on the running system; the gate-6 Critical route fix confirmed by in-browser navigation.
