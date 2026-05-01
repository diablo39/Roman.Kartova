# Kartova Design System

> **Note (2026-05-01):** Color and typography token values are deferred to Untitled UI defaults per ADR-0092. This document retains nav structure, layout density, and information-density rules — those remain canonical.

## Brand Identity
- **Product:** Kartova — Service Catalog & Developer Portal
- **Tagline:** Map your entire service landscape
- **Personality:** Professional, technical, trustworthy, clean. Feels like a modern developer tool — not playful, not corporate-stiff. Think GitHub meets Datadog.
- **Target users:** Software developers, DevOps engineers, engineering managers

## Color Palette

### Light Mode
- **Primary:** #2563EB (Blue 600 — trust, navigation, CTAs)
- **Primary Hover:** #1D4ED8 (Blue 700)
- **Secondary:** #7C3AED (Violet 600 — accents, async/event-driven entities)
- **Surface:** #FFFFFF
- **Surface Elevated:** #F8FAFC (Slate 50 — cards, panels)
- **Border:** #E2E8F0 (Slate 200)
- **Text Primary:** #0F172A (Slate 900)
- **Text Secondary:** #64748B (Slate 500)
- **Text On Primary:** #FFFFFF

### Dark Mode
- **Primary:** #3B82F6 (Blue 500)
- **Primary Hover:** #60A5FA (Blue 400)
- **Secondary:** #8B5CF6 (Violet 500)
- **Surface:** #0F172A (Slate 900)
- **Surface Elevated:** #1E293B (Slate 800)
- **Border:** #334155 (Slate 700)
- **Text Primary:** #F1F5F9 (Slate 100)
- **Text Secondary:** #94A3B8 (Slate 400)

### Semantic Colors
- **Success:** #10B981 (Emerald 500 — Operational, healthy, L5)
- **Warning:** #F59E0B (Amber 500 — Degraded, medium risk, L3)
- **Error:** #EF4444 (Red 500 — Outage, critical risk, breaking change)
- **Info:** #3B82F6 (Blue 500 — informational badges)
- **Manual Relationship:** #7C3AED (Violet — visually distinct from auto-discovered)
- **Auto-Discovered:** #94A3B8 (Slate 400 — subdued, system-managed)

### Entity Type Colors
- **Application:** #2563EB (Blue)
- **Service:** #0891B2 (Cyan 600)
- **API Sync:** #059669 (Emerald 600)
- **API Async:** #7C3AED (Violet 600)
- **Infrastructure:** #D97706 (Amber 600)
- **Message Broker:** #DC2626 (Red 600)
- **Queue/Topic:** #E11D48 (Rose 600)
- **Environment:** #4F46E5 (Indigo 600)
- **Deployment:** #0D9488 (Teal 600)

## Typography
- **Font Family:** Inter (headings and body) — clean, highly legible, developer-friendly
- **Monospace:** JetBrains Mono — for code, API paths, CLI commands, entity IDs
- **Scale:**
  - Display: 36px / 700 weight / -0.02em tracking
  - H1: 30px / 700 weight / -0.01em
  - H2: 24px / 600 weight
  - H3: 20px / 600 weight
  - H4: 16px / 600 weight
  - Body: 14px / 400 weight / 1.5 line-height
  - Small: 12px / 400 weight
  - Code: 13px / JetBrains Mono / 400 weight

## Spacing & Layout
- **Base unit:** 4px
- **Spacing scale:** 4, 8, 12, 16, 20, 24, 32, 40, 48, 64, 80, 96
- **Border radius:** 6px (small/buttons), 8px (cards), 12px (modals/panels), 9999px (pills/badges)
- **Content max-width:** 1280px
- **Sidebar width:** 260px (collapsible)
- **Page padding:** 24px (desktop), 16px (mobile)

## Component Patterns

### Navigation (MUST be identical on every screen)

**Top bar (fixed, always visible):**
- Height: 56px
- Background: Slate 900 (#0F172A) with 1px bottom border Slate 700
- Left: "Kartova" logo text (Inter 600, 18px, white) with small map-pin icon in Primary Blue
- Center: Search bar (width 480px, height 36px, Slate 800 background, Slate 600 border, 6px radius, placeholder "Search entities..." in Slate 500)
- Right: Notification bell icon (20px, Slate 400) with red badge for unread count, 16px gap, user avatar (32px circle) with dropdown chevron

**Left sidebar (fixed, collapsible):**
- Width expanded: 260px | Width collapsed: 64px (icons only)
- Background: Slate 800 (#1E293B) with 1px right border Slate 700
- Padding: 12px horizontal, 8px vertical between items
- Groups separated by 1px Slate 700 divider with 16px vertical spacing
- Group 1: Catalog (grid icon), Dependencies (git-branch icon), Teams (users icon)
- Group 2 "Dashboards" (expandable): Overview, Environment Map, Status Board
- Group 3: Status Page (globe icon), Settings (gear icon)
- Active item: Primary Blue (#2563EB) background with 6px radius, white text
- Inactive item: Slate 400 text, hover → Slate 700 background
- Collapse toggle: Bottom of sidebar, chevron-left icon (chevron-right when collapsed)

**Breadcrumbs (on detail pages only):**
- Position: Top of main content area, above page title
- Format: Org > Team > System > Entity, separated by "/" in Slate 500
- Each segment clickable (Slate 400, hover → white)
- Font: 12px Inter 400

### Cards
- Entity cards in list/grid views with: type icon + color, name (bold), owner/team, health badge, tags (pills), scorecard indicator
- Elevated surface background, 1px border, 8px radius, subtle hover shadow

### Badges & Status Indicators
- **Health:** Colored dot + label (Operational/Degraded/Outage/Unknown)
- **Maturity:** L1-L5 level badge with progress ring
- **Risk:** Color-coded pill (Critical=red, High=orange, Medium=amber, Low=green)
- **Relationship origin:** "Manual" = violet outline badge with pin icon, "Auto" = gray badge with robot icon
- **Entity type:** Colored icon matching entity type color palette
- **Tags:** Rounded pills, light background with colored text

### Data Visualization
- **Dependency graph:** Force-directed graph with entity-colored nodes, directional edges, manual edges = solid violet, auto = dashed gray
- **Environment matrix:** Table with Service rows x Environment columns, version in cells, color-coded freshness
- **Scorecards:** Circular progress indicator with score, category breakdown bars below
- **Status board:** Grid of tiles, health-colored borders, sortable
- **Tech Radar:** Concentric rings (Adopt/Trial/Assess/Hold) with quadrant sectors

### Forms & Inputs
- Clean, minimal inputs with label above, placeholder text, 6px radius
- Primary buttons: filled primary color, white text
- Secondary buttons: outlined, primary color border and text
- Destructive buttons: filled red

## Layout Templates

### Catalog List Page
- Top: Page title + "Register New" button + view toggle (list/grid) + filters
- Left: Faceted filter sidebar (type, team, tags, health)
- Main: Entity cards in list or grid layout
- Pagination at bottom

### Entity Detail Page
- Header: Entity type icon + name + health badge + maturity level + action buttons
- Tab bar: Overview | APIs | Documentation | Dependencies | Deployments | Settings
- Overview tab: Metadata card, relationship summary, scorecard, recent activity
- Full-width below tabs

### Dashboard Page
- Top: Dashboard title + date range selector + filters
- Grid of metric cards (KPIs at top)
- Charts and visualizations below in 2-column grid
- Full-width tables/lists at bottom

### Status Page (Public)
- Minimal chrome: brand logo + title only
- Overall status banner (green/yellow/red)
- Component groups with individual status indicators
- Uptime bars (90 days)
- Incident history timeline at bottom
- Subscribe button top-right

## Iconography
- **Style:** Outlined, 1.5px stroke, 20x20 default size
- **Icon set:** Lucide Icons (consistent with modern dev tools)
- **Entity icons:** Custom per entity type, matching entity color

## Accessibility
- WCAG 2.1 AA compliant contrast ratios
- Focus rings on all interactive elements (2px offset, primary color)
- Keyboard navigable: all actions reachable via Tab/Enter/Escape
- Screen reader labels on all icons and badges

## Responsive Breakpoints
- **Mobile:** < 640px (single column, bottom nav)
- **Tablet:** 640-1024px (collapsed sidebar, 2-column grid)
- **Desktop:** > 1024px (full sidebar, multi-column layout)

## Animation & Motion
- **Duration:** 150ms (micro-interactions), 200ms (panel transitions), 300ms (page transitions)
- **Easing:** ease-out for entrances, ease-in for exits
- **Principles:** Subtle and functional only — no decorative animations
