```markdown
# Design System Specification: Technical Elegance

## 1. Overview & Creative North Star
**Creative North Star: The Sovereign Architect**
This design system moves beyond the generic "SaaS dashboard" aesthetic to create a high-density, authoritative environment for engineers. It is designed to feel like a sophisticated instrument—precise, powerful, and intentionally understated. 

By rejecting standard "boxed" layouts in favor of **Tonal Layering**, we create an expansive digital workshop. The goal is to present immense amounts of data (services, dependencies, and health metrics) without overwhelming the user, achieved through a "Soft Brutalist" approach: rigid logic meets sophisticated, airy depth.

---

## 2. Colors & Surface Logic
The palette is rooted in deep, obsidian slates, using light not as a border, but as a volume.

### The "No-Line" Rule
**Explicit Instruction:** Designers are prohibited from using 1px solid borders for sectioning or containment. Structural boundaries must be defined solely through background color shifts.
*   **Method:** Place a `surface_container_high` card on a `surface_low` background. The eye perceives the edge through the shift in value, creating a cleaner, more premium interface.

### Surface Hierarchy & Nesting
Treat the UI as a series of physical layers. Use the following tokens to create "nested" depth:
*   **Base Layer:** `surface` (#0b1326) – The infinite void.
*   **Sectioning:** `surface_container_low` (#131b2e) – Large layout areas (Sidebar, Main Feed).
*   **Primary Containers:** `surface_container` (#171f33) – Standard entity cards.
*   **Elevated Focus:** `surface_container_highest` (#2d3449) – Hover states and active modals.

### The "Glass & Gradient" Rule
To prevent a "flat" appearance, use **Glassmorphism** for floating elements (e.g., dropdowns, tooltips). Use `surface_bright` at 60% opacity with a `20px` backdrop-blur. 
*   **Signature CTA:** Apply a subtle linear gradient to Primary Buttons (from `primary_container` #2563eb to `primary` #b4c5ff at 10% opacity overlay) to provide a "metallic" technical sheen.

---

## 3. Typography: The Editorial Scale
We pair the utilitarian precision of **Inter** with the industrial soul of **JetBrains Mono**.

*   **Display (Inter):** Use `display-md` (2.75rem) for high-level health overviews. Apply `-0.02em` letter spacing to maintain a "tight" editorial feel.
*   **Headlines (Inter):** `headline-sm` (1.5rem) should be Semi-Bold to establish immediate section authority.
*   **Code & Metadata (JetBrains Mono):** All Service IDs, ARNs, and Terminal outputs must use JetBrains Mono at `body-sm` (0.75rem). This signals "raw data" vs. "UI labels."
*   **The "Contextual Small" Rule:** Use `label-sm` (uppercase with 0.05em tracking) for category headers to create a professional, technical hierarchy.

---

## 4. Elevation & Depth
In this system, elevation is a product of **Tonal Stacking**, not shadows.

*   **The Layering Principle:** Depth is achieved by placing lighter containers on darker backgrounds. For example: A `surface_container_high` modal sitting on a `surface_dim` backdrop.
*   **Ambient Shadows:** For floating elements only, use a "Ghost Shadow": 
    *   `box-shadow: 0 24px 48px -12px rgba(0, 0, 0, 0.5);`
    *   The shadow should never be pure black, but rather a deeper tint of the background color.
*   **The "Ghost Border" Fallback:** If a separation is required for accessibility, use `outline_variant` (#434655) at **15% opacity**. This creates a suggestion of a line without introducing visual noise.

---

## 5. Components

### Navigation Architecture
*   **Top Bar:** Fixed 56px. Background: `surface` (#0b1326). Use a `surface_container_high` 1px bottom "Ghost Border."
*   **Sidebar:** 260px (collapsible). Background: `surface_container_low` (#131b2e). Icons should be `on_surface_variant` (#c3c6d7), shifting to `primary` (#b4c5ff) on active states.

### Entity Cards (Information-Dense)
*   **Styling:** No borders. Background: `surface_container`.
*   **Layout:** Use `title-sm` for service names. 
*   **Forbid Dividers:** Do not use lines between rows. Use 16px of vertical whitespace or a 4px `surface_container_low` gutter.

### Health & Maturity Badges
*   **Healthy:** `secondary_container` with `on_secondary` text.
*   **Warning:** `tertiary_container` with `on_tertiary` text.
*   **Critical:** `error_container` with `on_error` text.
*   **Shape:** Use `rounded-full` (9999px) for status pills to contrast against the `0.25rem` radius of the cards.

### Buttons & Inputs
*   **Primary Action:** Background: `primary_container` (#2563eb). Text: `on_primary_container`. Shape: `md` (0.375rem).
*   **Input Fields:** Background: `surface_container_lowest`. On focus, transition the "Ghost Border" from 15% opacity to 100% `primary`.

---

## 6. Do's and Don'ts

### Do
*   **DO** use JetBrains Mono for any string that could be copied into a terminal.
*   **DO** utilize `surface_container` variations to group related content rather than using lines.
*   **DO** leave ample "Director's Gap" (32px+) between major sections to let the technical data breathe.
*   **DO** use `surface_bright` for hover states on list items to create a "glow" effect.

### Don't
*   **DON'T** use 1px solid borders at 100% opacity. It shatters the "glass and stone" aesthetic.
*   **DON'T** use standard blue for links within body text; use `primary` (#b4c5ff) with a subtle underline to ensure it pops against the Slate 900 background.
*   **DON'T** use pure white (#FFFFFF) for text. Always use `on_surface` (#dae2fd) to reduce eye strain in the default dark mode.
*   **DON'T** use rounded corners larger than `0.5rem` (lg) for functional containers; keep the edges sharp and professional.

---
**Director's Final Note:** 
This system is a tool for developers. It should feel like a high-end IDE—efficient, dark-themed, and incredibly precise. When in doubt, remove a line and add a background tint shift.```