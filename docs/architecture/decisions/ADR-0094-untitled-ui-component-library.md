# ADR-0094: Untitled UI Free-Tier as Primary UI Primitive Layer

**Status:** Accepted
**Date:** 2026-05-01
**Deciders:** Roman Głogowski (solo developer)
**Category:** Frontend Architecture
**Related:** ADR-0039, ADR-0040, ADR-0084, ADR-0087, ADR-0088 (superseded by this)

## Context

Slice-4's first-cut catalog UI was built on shadcn/ui + Radix (ADR-0088). The footprint is small (5 consumer files, 17 owned primitives). The author prefers Untitled UI's visual language and is comfortable accepting Untitled's tokens as the new design-token source of truth, deferring DESIGN.md color/typography references for now.

## Decision

Adopt **Untitled UI free-tier** as the primary UI primitive layer:

- Install via `npx untitledui@latest add <name>`; components land in `web/src/components/<category>/<group>/`.
- Token vocabulary comes from `untitleduico/react/styles/theme.css` (committed to `web/src/styles/theme.css`).
- Icons via `@untitledui/icons` everywhere.
- Hand-roll only `card.tsx` and `skeleton.tsx` (not in free tier; ~15 LoC total).
- Keep `sonner` for toasts (Untitled does not ship a toast).
- Dark mode via `.dark-mode` class; rebind `next-themes` to set this class.

## Rationale

- Visual language preferred by the project owner.
- Free tier covers 15 of 17 in-use shadcn primitives; 2 are trivially hand-rolled.
- Single coherent design system; no shadcn/Untitled coexistence.
- PRO upgrade path remains available later for larger templates.

## Alternatives Considered

- **Stay on shadcn/ui (status quo, ADR-0088):** rejected — owner preference for Untitled.
- **Coexistence (shadcn for slice-4, Untitled for new screens):** rejected — slice-4 is small enough that a clean cut beats two parallel design languages.
- **Untitled UI PRO:** deferred — free tier covers slice-4 needs.

## Consequences

**Positive:**
- One coherent design language across the SPA.
- Untitled's CLI-driven workflow installs deps automatically.
- Token vocabulary maintained upstream.

**Negative / Trade-offs:**
- DESIGN.md color/typography references go silent until a future re-skin slice.
- `card` and `skeleton` are hand-rolled and may drift from Untitled's evolution.
- RAC API differs from Radix; `RegisterApplicationDialog` requires a structural restructure (not a one-line swap).
