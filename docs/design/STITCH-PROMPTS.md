# Kartova — Google Stitch Prompts

## How to Use This File

### Prerequisites
- Google account with access to [stitch.withgoogle.com](https://stitch.withgoogle.com)
- [DESIGN.md](DESIGN.md) file ready (contains all design tokens, colors, typography, navigation specs)

### Setup (do this once per new Stitch project)

1. **Open Stitch** and create a new project
2. **Load the design system:** Select any element on canvas > click **"Modify"** > choose **"Design System"** from dropdown > paste the full contents of [DESIGN.md](DESIGN.md) into the custom instructions field
3. **Paste Step 0** (Context Prompt below) as your first generation to establish the product context
4. **Generate Screen 1** (Navigation Reference) — this is your master layout. All other screens must match it.
5. **Generate remaining screens** in order (Screen 2-8), each one referencing Screen 1

### Rules for Consistent Results

- **Generate one screen at a time** — do not combine multiple screens in one prompt
- **Use Standard mode** (Gemini 2.5 Flash, 350 gen/month) for iteration, **Experimental mode** (Gemini 2.5 Pro, 50 gen/month) for final polished versions
- **Fix inconsistencies immediately** with micro-prompts before moving to next screen (e.g., "Match the sidebar exactly to Screen 1")
- **Use Annotate** (draw/circle on canvas) to point at specific elements that need fixing
- **5 small prompts beat 1 mega prompt** — iterate incrementally
- **After each screen:** visually compare navigation (top bar, sidebar) to Screen 1. If anything differs, fix it before proceeding.
- **Export to Figma** after finalizing each screen for design review
- **Update DESIGN.md** if Stitch produces a good pattern not yet captured — then re-paste into Design System settings

### Consistency Enforcement Block

Every prompt below (except Step 0, Screen 1, and Screen 5) includes a **CONSISTENCY BLOCK** at the top. This block forces Stitch to reuse the exact same navigation shell. Do not remove or modify it.

---

## Step 0: Context Prompt

> Paste this first in a new Stitch project. It sets the product context for all future generations.

```
I'm building Kartova — a SaaS service catalog and developer portal for 
tracking applications, services, APIs, infrastructure, dependencies, 
health status, and documentation. Think Backstage meets Datadog meets 
Atlassian Statuspage.

Target users: software developers, DevOps engineers, engineering managers.

Visual style: Professional, clean, modern developer tool. Dark mode as 
default. Similar vibe to Linear, GitHub, or Datadog — information-dense 
but not cluttered.

Typography: Inter for all UI text, JetBrains Mono for code snippets, 
entity IDs, API paths, and CLI commands.

I've loaded my DESIGN.md as the Design System. It contains exact pixel 
values for navigation, colors, spacing, and component patterns. Treat it 
as the single source of truth. Every screen you generate MUST follow 
the navigation layout defined there — same top bar, same sidebar, same 
colors, same dimensions. No variations between screens.
```

---

## Screen 1: Navigation Reference (GENERATE THIS FIRST)

> This is your master layout. Every other screen must match this navigation pixel-for-pixel. Keep this screen on your canvas as a reference.

```
Design ONLY the navigation shell for Kartova. Dark mode. No page content.
This is the MASTER REFERENCE that all other screens must match exactly.

Top bar (56px height, fixed, Slate 900 #0F172A background, 1px bottom 
border Slate 700 #334155):
- Left: Small map-pin icon in blue (#2563EB) + "Kartova" text 
  (Inter 600, 18px, white)
- Center: Search bar (480px wide, 36px tall, Slate 800 #1E293B background, 
  1px Slate 600 border, 6px radius, placeholder "Search entities..." 
  in Slate 500 #64748B)
- Right: Bell icon (20px, Slate 400 #94A3B8) with small red circle badge 
  showing "3", then 16px gap, then user avatar (32px circle, placeholder 
  photo) with small dropdown chevron

Left sidebar (260px wide, fixed, Slate 800 #1E293B background, 1px right 
border Slate 700 #334155):
- Navigation items with icon (20px) + label, 12px horizontal padding, 
  8px vertical gap between items
- Group 1: "Catalog" (grid icon, ACTIVE — blue #2563EB background, 
  6px radius, white text), "Dependencies" (git-branch icon, inactive — 
  Slate 400 text), "Teams" (users icon, inactive)
- Divider: 1px line Slate 700, 16px vertical spacing
- Group 2 header: "Dashboards" (expandable, chevron-down), sub-items 
  indented: "Overview", "Environment Map", "Status Board" — all inactive
- Divider: 1px line Slate 700, 16px vertical spacing
- Group 3: "Status Page" (globe icon, inactive), "Settings" (gear icon, 
  inactive)
- Bottom: Collapse toggle — chevron-left icon in Slate 500

Main content area: Empty, Slate 900 #0F172A background. Centered text: 
"Main content area" in Slate 600, just as placeholder.

Generate TWO versions side by side:
1. Sidebar expanded (260px)
2. Sidebar collapsed (64px, icons only, no labels)
```

---

## Screen 2: Catalog Home Dashboard

```
CONSISTENCY RULES (mandatory — do not override):
- Use EXACTLY the same top bar and left sidebar as Screen 1 
  (Navigation Reference) on this canvas. Same dimensions, same colors, 
  same items, same spacing. Do not redesign the navigation.
- Active sidebar item: "Catalog" (blue background, white text)
- Sidebar state: expanded (260px)
- Dark mode. Follow DESIGN.md for all colors and spacing.

PAGE CONTENT — Catalog Home Dashboard:

Breadcrumbs: None (this is the top-level page).
Page title: "Catalog" (H2, 24px Inter 600, white) with right-aligned 
"+ Register Entity" button (primary blue, white text, 6px radius).

Content sections in the main area:

1. Stats row (4 cards in a horizontal row, equal width):
   - "Applications: 124" with blue icon, "+3 this week" trend in green
   - "Services: 89" with cyan icon, "+1 this week" trend in green
   - "APIs: 203" with green icon, "No change" trend in Slate 500
   - "Infrastructure: 47" with amber icon, "-2 this week" trend in red
   Cards: Slate 800 background, 1px Slate 700 border, 8px radius

2. "My Team's Components" section (H3 title, below stats):
   List of 5 entity cards, each showing:
   - Left: entity type colored icon (circle, 32px)
   - Name in bold white (14px Inter 600)
   - Owner/team badge (small, Slate 600 background, Slate 300 text)
   - Health dot: 3 green, 1 yellow, 1 red — with label text
   - 2-3 tag pills (small, rounded, colored background matching tag category)
   - Right-aligned: Scorecard "78%" in small circular indicator
   Cards: Slate 800 background, 1px Slate 700 border, 8px radius, 
   hover state with subtle lighter background

3. Two-column section below:
   Left column — "Recent Activity" (H3):
   - Timeline feed, 5 items: deployment icon + "payment-gateway v2.1.3 
     deployed to prod" + "2h ago" + actor name. Alternating item types: 
     deployment, entity registered, relationship added.
   Right column — "Needs Attention" (H3):
   - 3 warning cards: low scorecard entity (amber border), high risk 
     entity (red border), pending approval from agent discovery (blue border)
   - Each card: entity name, issue description, action button
```

## Screen 3: Entity Detail Page (Service)

```
CONSISTENCY RULES (mandatory — do not override):
- Use EXACTLY the same top bar and left sidebar as Screen 1 
  (Navigation Reference) on this canvas. Same dimensions, same colors, 
  same items, same spacing. Do not redesign the navigation.
- Active sidebar item: "Catalog" (blue background, white text)
- Sidebar state: expanded (260px)
- Dark mode. Follow DESIGN.md for all colors and spacing.

PAGE CONTENT — Entity Detail for service "payment-gateway":

Breadcrumb: "MyOrg / Platform Team / Payments System / payment-gateway" 
(12px Inter 400, Slate 400 text, "/" separators in Slate 500, each 
segment clickable)

Header section (below breadcrumb, full width):
- Left: Cyan circle icon (Service type) + "payment-gateway" (H1, 30px 
  Inter 700, white)
- Badges row (below title, 8px gap between):
  - Health: green dot + "Operational" (green text, Slate 800 bg pill)
  - Maturity: "L4" badge with small progress ring + "Operationally Ready"
  - Risk: "Low" (green pill, small)
  - DX Score: "82" in small circular progress indicator
- Right-aligned: "Edit" button (secondary, outlined), "View in Graph" 
  button (secondary), three-dot menu icon

Tab bar (below header, 16px top margin, full width):
  Overview (active, blue underline) | APIs (3) | Documentation | 
  Dependencies | Deployments | Settings
  Tabs: 14px Inter 500, active = white + blue 2px underline, 
  inactive = Slate 400, 24px horizontal padding per tab

Overview tab content (below tab bar):

Left column (2/3 width):
  - Metadata card: two-column key-value layout
    Description: "Processes all payment transactions and manages payment 
    provider integrations"
    Language: "C#" (monospace) | Framework: ".NET 8" | Repo: link icon + 
    "github.com/acme/payment-gateway" (blue, clickable)
    Created: "Jan 15, 2025" | Last deployed: "2 hours ago"
    Card: Slate 800 bg, 1px Slate 700 border, 8px radius

  - Relationship summary card (below metadata):
    "8 dependencies, 12 dependents" in Slate 300
    Mini dependency graph preview: 4-5 nodes with edges, simplified, 
    dark background, ~200px height. Center node cyan.
    "View all dependencies" link (blue, small arrow icon)

  - Recent deployments table (below):
    3 rows: Version | Environment | Date | Deployer
    "v2.1.3" | "prod" (green badge) | "2h ago" | avatar + "jane.doe"
    "v2.1.3" | "staging" (yellow badge) | "1d ago" | avatar + "john.smith"
    "v2.1.2" | "prod" (green badge) | "5d ago" | avatar + "jane.doe"
    Table: Slate 800 bg, Slate 700 borders, 8px radius

Right column (1/3 width):
  - Ownership card: "Platform Team" with team icon, 3 member avatars 
    (overlapping circles), "5 members" link, Slack icon + "#platform-team"
  - Tags section: pills — "tier-1" (red bg), "payments" (blue bg), 
    "pci" (amber bg), "dotnet" (purple bg)
  - Scorecard card: 4 horizontal bars:
    Documentation 90% (green), Operations 85% (green), 
    Security 75% (amber), Quality 80% (green)
    Overall: "82%" in larger text
  - Quick Links card: Grafana Dashboard (external link icon), 
    Runbook (doc icon), On-Call Schedule (calendar icon)
```

## Screen 4: Dependencies Tab (Entity Detail)

```
CONSISTENCY RULES (mandatory — do not override):
- Use EXACTLY the same top bar and left sidebar as Screen 1 
  (Navigation Reference) on this canvas. Same dimensions, same colors, 
  same items, same spacing. Do not redesign the navigation.
- Active sidebar item: "Catalog" (blue background, white text)
- Sidebar state: expanded (260px)
- Dark mode. Follow DESIGN.md for all colors and spacing.

PAGE CONTENT — "Dependencies" tab on entity detail page for 
"payment-gateway" service:

Breadcrumb and header: Same as Screen 3 (Entity Detail), but the 
active tab is now "Dependencies" (blue underline) instead of "Overview".

Tab content:

Top summary row (horizontal, Slate 800 background card, 8px radius):
- "8 upstream dependencies" (with up-arrow icon)
- Vertical divider (1px Slate 700)
- "12 downstream dependents" (with down-arrow icon)
- Vertical divider
- "3 manual" (violet text, pin icon) + "17 auto-discovered" (Slate 400, 
  robot icon)
- Right-aligned: "Open full graph" button (primary blue, expand icon) — 
  this is the main CTA linking to standalone /graph page

Embedded mini dependency graph (~55% of remaining content height):
- Dark background (Slate 950 or very dark) with subtle dot grid pattern
- Center node: "payment-gateway" — cyan circle (40px), white label below, 
  green health dot
- 1 level deep only:
  Upstream (left side, 3 nodes): 
    "auth-service" (cyan/service), "PostgreSQL-main" (amber/infrastructure), 
    "Redis-cache" (amber/infrastructure)
  Downstream (right side, showing 3 of 5): 
    "checkout-ui" (blue/application), "order-service" (cyan/service), 
    "notification-service" (cyan/service), then a "+2 more" pill node 
    (Slate 600, rounded)
- Edges: 
  auth-service → payment-gateway: solid violet line (manual, small pin icon)
  PostgreSQL-main → payment-gateway: dashed gray line (auto-discovered)
  All others: dashed gray (auto)
- Nodes have: colored circle + name label + small health dot (green/yellow)
- No side panel. No filter controls. Basic zoom buttons only (bottom-right).

Relationship table (below graph, remaining space):
- Two-tab selector: "Dependencies (8)" (active) | "Dependents (12)"
- Above table, right-aligned: "+ Add Relationship" button (secondary, outlined)
- Table columns: Entity Name | Type | Relationship | Origin | Health
  Row 1: "auth-service" | Service (cyan badge) | depends-on | 
    Manual (violet pin badge) | Operational (green dot)
  Row 2: "PostgreSQL-main" | Infrastructure (amber badge) | depends-on | 
    Auto (gray robot badge) | Operational (green dot)
  Row 3: "Redis-cache" | Infrastructure (amber badge) | depends-on | 
    Auto (gray robot badge) | Operational (green dot)
  (5 more rows with similar pattern)
- Rows clickable (hover: Slate 700 background)
- Table: Slate 800 bg, Slate 700 cell borders, 8px radius outer
```

## Screen 5: Standalone Dependency Graph Explorer

```
CONSISTENCY RULES (mandatory — do not override):
- Use EXACTLY the same top bar as Screen 1 (Navigation Reference) on 
  this canvas. Same dimensions, same colors, same spacing.
- Left sidebar: COLLAPSED state (64px wide, icons only, no labels). 
  Active item: "Dependencies" (git-branch icon, blue background circle).
- Dark mode. Follow DESIGN.md for all colors and spacing.

PAGE CONTENT — Standalone Dependency Graph Explorer (/graph):

This is a full-width page. The sidebar is collapsed to maximize graph space.

Below the top bar, a secondary toolbar (48px height, Slate 800 bg, 
1px bottom border Slate 700):
- Left: "Dependency Graph" (H2, 20px Inter 600, white)
- Center: Entity search field (360px wide, autocomplete dropdown style, 
  showing currently focused entity: cyan dot + "payment-gateway" with 
  small X to clear). Typing searches all entities.
- Right: Filter pills — "Team: All" dropdown, "Domain: All" dropdown, 
  "Criticality: All" dropdown, "Type: All" dropdown. Then toggle button 
  group: "All" (active, filled) | "Manual" | "Auto". Then "Reset" text 
  link in Slate 500.

Main graph area (remaining viewport height minus top bar and toolbar):
- Dark background (Slate 950 #020617) with subtle dot grid pattern 
  (dots in Slate 800, 24px spacing)
- Center node: "payment-gateway" — larger circle (48px, cyan), glowing 
  subtle cyan border (2px, 50% opacity), white label below, green health dot
- Upstream (left/top): "auth-service" (32px cyan), "PostgreSQL-main" 
  (32px amber), "Redis-cache" (32px amber)
- Downstream (right/bottom): "checkout-ui" (32px blue), "order-service" 
  (32px cyan, SELECTED — dashed selection ring), "notification-service" 
  (32px cyan), "reporting-api" (32px green), "mobile-app" (32px blue)
- Second-level connections: 2 dimmed nodes connected to "order-service", 
  visible but at 40% opacity (showing depth = 2)
- Edges: Manual = solid violet 2px lines with small pin icon at midpoint. 
  Auto = dashed gray 1px lines with small robot icon at midpoint.
- Each node: colored circle + white name label below + health dot 
  (top-right, 8px)

Floating controls (bottom-left, Slate 800 bg card, 8px radius, 
semi-transparent):
- Zoom: "+" and "-" buttons, "Fit" button (fit-to-view icon)
- Depth: segmented toggle "1 | 2 | 3 | All" (currently "2" active)
- Fullscreen toggle button

Side panel (right, 320px width, slides in from right because 
"order-service" node is selected):
- Background: Slate 800, 1px left border Slate 700
- Close button (X) top-right
- Header: Cyan circle + "order-service" (H3, 20px Inter 600) + 
  "Operational" green badge
- Metadata list (key-value, 14px):
  Team: "Commerce Team" | Owner: "alice.chen" | Maturity: "L3" badge | 
  Tags: "tier-2", "commerce" pills
- Stats: "4 dependencies, 6 dependents"
- Relationship line: "depends-on → payment-gateway" with "Auto" gray badge
- Divider (1px Slate 700)
- Buttons (stacked, full width, 8px gap):
  "View Entity Detail" (primary blue button)
  "Impact Analysis" (secondary outlined button)
  "Focus Graph Here" (tertiary, text-only, Slate 400)

Bottom legend bar (fixed, 40px height, Slate 800 bg, 1px top border 
Slate 700):
- Left: Entity type legend — colored dots with labels: Application (blue), 
  Service (cyan), API Sync (green), API Async (violet), Infrastructure 
  (amber), Broker (red), Queue (rose)
- Center: Relationship legend — solid violet line + "Manual" label, 
  dashed gray line + "Auto" label
- Right: "Showing 9 of 847 entities" counter in Slate 500
```

## Screen 6: Self-Service Onboarding Wizard (Step 3)

```
CONSISTENCY RULES (mandatory — do not override):
- Use EXACTLY the same top bar as Screen 1 (Navigation Reference).
- Left sidebar: HIDDEN (not visible). The onboarding wizard is a 
  full-width experience with no sidebar. The user hasn't set up their 
  org yet, so sidebar navigation doesn't apply.
- Dark mode. Follow DESIGN.md for all colors and spacing.

PAGE CONTENT — Onboarding Wizard, Step 3: Review Discovered Entities:

Progress indicator (centered, below top bar, 48px height):
- 4 steps in a horizontal line connected by lines:
  1. "Create Org" — green circle with white checkmark, green label
  2. "Connect Git" — green circle with white checkmark, green label
  3. "Review Entities" — blue filled circle with "3" number, white label 
     (ACTIVE, slightly larger)
  4. "Confirm Import" — Slate 600 empty circle with "4", Slate 500 label
- Connecting lines: green (completed), blue (active to next), 
  Slate 600 (upcoming)

Main content (centered, max-width 960px, 32px top padding):

Title: "We found 47 entities across 12 repositories" (H1, 30px Inter 700)
Subtitle: "Review what we discovered. Edit, remove, or add before 
importing." (16px Inter 400, Slate 400)

Results list (24px below subtitle), grouped by repository:

Repository section 1 — EXPANDED:
- Header row: folder icon + "payment-gateway" (H3, 16px Inter 600) + 
  "5 entities" badge (Slate 600 bg) + chevron-up icon to collapse
- Entity rows (indented 24px, 4px gap between):
  Row: [checkbox checked] + cyan circle icon + "payment-gateway" 
    (bold) + "Application — C#, .NET 8" (Slate 400) + pencil edit icon
  Row: [checkbox checked] + green circle icon + "Payment API" 
    (bold) + "API (Sync) — OpenAPI 3.0 detected" + pencil icon
  Row: [checkbox checked] + violet circle icon + "Payment Events" 
    (bold) + "API (Async) — AsyncAPI 2.6 detected" + pencil icon
  Row: [checkbox checked] + amber circle icon + "PostgreSQL" 
    (bold) + "Infrastructure — connection string detected" + pencil icon
  Row: [checkbox checked] + rose circle icon + "payment.completed" 
    (bold) + "Queue — RabbitMQ topic" + pencil icon
  Each row: Slate 800 bg, 1px Slate 700 border, 6px radius, 
  hover: lighter background

Repository section 2 — COLLAPSED:
- Header: folder icon + "order-service" + "6 entities" badge + 
  chevron-down icon
  
Repository section 3 — COLLAPSED:
- Header: folder icon + "notification-service" + "4 entities" badge + 
  chevron-down

Warning banner (below list, if applicable):
- Amber left border (4px), Slate 800 bg, amber warning icon
- "3 entities have low confidence detections. Review recommended."
- Expandable (chevron to show which entities)

Fixed bottom bar (64px height, Slate 800 bg, 1px top border Slate 700):
- Left: "47 of 47 selected" + "Select All" / "Deselect All" text links 
  (blue, 14px)
- Right: "Back" button (secondary outlined) + "Confirm & Import" button 
  (primary blue, larger)
```

## Screen 7: Public Status Page

> This screen is DIFFERENT from all others — it's customer-facing, light mode, no Kartova admin navigation.

```
This screen does NOT use the Kartova admin navigation. It is a 
PUBLIC-FACING status page for end customers. Light mode. Minimal chrome.
Completely separate visual context from the admin portal.

Design a public Status Page for a company called "Acme Corp".

Background: white (#FFFFFF). Text: Slate 900 (#0F172A).

Header (centered, 80px height, white bg, subtle bottom shadow):
- Left: Acme Corp logo placeholder (40px square, gray)
- Center: "System Status" (H1, 24px Inter 700, Slate 900)
- Right: "Subscribe to Updates" button (outlined, blue border, blue text, 
  6px radius, bell icon)

Overall status banner (full width, below header):
- Version A (show this): Green background (#10B981), white text, 
  checkmark icon + "All Systems Operational" (18px Inter 600)
- Version B (show below as alternative): Red background (#EF4444), 
  white text, alert icon + "Partial System Outage"

Component groups (max-width 800px, centered, 24px padding):

Group "Core Services" (expanded):
  Header: "Core Services" (H3, 16px Inter 600, Slate 900) + chevron-up
  Items (white bg cards, 1px Slate 200 border, 6px radius, 8px gap):
  - "Payment Processing" — right-aligned: green dot + "Operational"
  - "User Authentication" — green dot + "Operational"
  - "Order Management" — yellow dot + "Degraded Performance" (amber text)

Group "Infrastructure" (expanded):
  - "API Gateway" — green dot + "Operational"
  - "CDN" — green dot + "Operational"

Group "Integrations" (collapsed):
  Header + "3 of 3 Operational" summary + chevron-down

Uptime section (below groups, 32px top margin):
  Title: "Uptime — Last 90 Days" (H3)
  Per component: horizontal bar (100% = 90 days), made of tiny segments. 
  Green = operational, yellow = degraded, red = outage. Right-aligned: 
  "99.98%" label. One bar per visible component.

Past Incidents section (below uptime, 32px top margin):
  Title: "Past Incidents" (H3)
  - "April 15, 2026 — Order Processing Delay"
    Status timeline: Investigating (red) → Identified (amber) → 
    Monitoring (blue) → Resolved (green). Duration: "47 minutes"
    Expandable for update details.
  - "April 10, 2026 — Scheduled Maintenance"
    Wrench icon, "Completed", 2h window.

Footer (centered, 48px, Slate 200 bg):
- "Powered by Kartova" (small link, Slate 500) | 
  "Last updated: 3 minutes ago" (Slate 400)
```

## Screen 8: Scorecard & DX Score

```
CONSISTENCY RULES (mandatory — do not override):
- Use EXACTLY the same top bar and left sidebar as Screen 1 
  (Navigation Reference) on this canvas. Same dimensions, same colors, 
  same items, same spacing. Do not redesign the navigation.
- Active sidebar item: "Catalog" (blue background, white text)
- Sidebar state: expanded (260px)
- Dark mode. Follow DESIGN.md for all colors and spacing.

PAGE CONTENT — This is a section/tab view within the entity detail page. 
Show the same breadcrumb and header as Screen 3 (Entity Detail for 
"payment-gateway"), but with a custom tab active. The tab bar shows:
Overview | APIs (3) | Documentation | Quality (active, blue underline) | 
Dependencies | Deployments | Settings

Quality tab content:

Top section — DX Score hero card (full width, Slate 800 bg, 8px radius):
- Left: Large circular progress ring (80px diameter). Score "72" inside 
  (32px Inter 700, white). Ring color: amber gradient (score is in 
  amber zone: 60-79). Ring track: Slate 700.
- Right of ring: "Developer Experience Score" (H3), 
  "72 / 100" (Slate 300), trend: green up arrow + "+5 from last month"
- Far right: "How is this calculated?" link (Slate 400, small question 
  mark icon)

Four category cards (horizontal row, equal width, 16px gap, below hero):
- Documentation: "85%" large text + green progress bar (full width of 
  card) + "7 of 8 checks passing" (Slate 400, small) + green checkmark
- Operations: "60%" + amber progress bar + "3 of 5 checks passing" + 
  amber warning icon
- Security: "75%" + green progress bar + "6 of 8 checks passing" + 
  green checkmark
- Quality: "65%" + amber progress bar + "4 of 6 checks passing" + 
  amber warning icon
Cards: Slate 800 bg, 1px Slate 700 border, 8px radius

Two-column layout below cards:

Left column (2/3 width) — "Recommended Actions" (H3):
- Sort: "By Impact" dropdown (right-aligned, small)
- Action items (list, Slate 800 bg cards, 6px radius, 8px gap):
  - [ ] "Add a runbook document" — "+12 pts" (green bold) — 
    "Operations" tag pill (amber) — "Learn more" link
  - [ ] "Configure alerting rules" — "+8 pts" — "Operations" tag
  - [ ] "Add getting-started guide" — "+6 pts" — "Documentation" tag (blue)
  - [ ] "Define SLA targets" — "+5 pts" — "Operations" tag
  Checkboxes: empty squares, Slate 600 border. Items have hover state.

Right column (1/3 width) — "Maturity Level" (H3):
- Current level card: "L3" large badge (amber circle, 48px) + 
  "Observable" label (H4) — "Level 3 of 5"
- Progress: "4 of 6 requirements met for L4" (Slate 300)
- Checklist for L4 requirements:
  - green checkmark + "Health checks configured"
  - green checkmark + "Monitoring set up"
  - green checkmark + "Incident response plan"
  - green checkmark + "SLA defined"
  - red X + "Runbook document" (Slate 400, strikethrough-style)
  - red X + "On-call rotation defined" (Slate 400)
Card: Slate 800 bg, 1px Slate 700 border, 8px radius
```

## Screen 9: Tech Radar

```
CONSISTENCY RULES (mandatory — do not override):
- Use EXACTLY the same top bar and left sidebar as Screen 1 
  (Navigation Reference) on this canvas. Same dimensions, same colors, 
  same items, same spacing. Do not redesign the navigation.
- Active sidebar item: "Dashboards" group expanded, no specific sub-item 
  highlighted (Tech Radar is a standalone view under Dashboards).
- Sidebar state: expanded (260px)
- Dark mode. Follow DESIGN.md for all colors and spacing.

PAGE CONTENT — Technology Radar page:

Page header:
- "Technology Radar" (H1, 30px Inter 700, white)
- Subtitle: "Auto-generated from 847 services across your organization. 
  Last updated: April 2026." (14px Inter 400, Slate 400)

Main visualization (centered, square aspect ratio, ~600x600px):
- Circular radar with 4 concentric rings on Slate 950 background:
  - Innermost ring: "Adopt" — brightest zone, subtle blue tint
  - Second ring: "Trial" — medium brightness
  - Third ring: "Assess" — dim
  - Outermost ring: "Hold" — dimmest, subtle red tint
- Ring labels at 12-o'clock position, Slate 500 text
- Four quadrants divided by thin Slate 700 lines:
  Top-right: "Languages & Frameworks" (label in Slate 400)
  Bottom-right: "Infrastructure"
  Bottom-left: "Data Management"
  Top-left: "Tools"

Technology dots on the radar (colored circles, size = service count):
- Adopt: .NET (large 20px dot, blue, 120 services), React (18px, blue, 95), 
  PostgreSQL (16px, amber, 88), Kubernetes (14px, cyan, 75), 
  RabbitMQ (12px, red, 45)
- Trial: Go (10px, cyan, 12), Kafka (10px, red, 8), Redis (12px, amber, 30)
- Assess: Rust (8px, cyan, 3), gRPC (8px, green, 5)
- Hold: jQuery (8px, Slate 400, 2, red glow ring), 
  .NET Framework 4.x (8px, Slate 400, 4, red glow ring)

Side panel (right, 320px, Slate 800 bg, 1px left border Slate 700):
- Header: ".NET" (H3, blue dot next to name, indicating selected)
- Ring: "Adopt" (blue badge)
- Services using: "120" (large, 24px, white)
- Teams using: "8"
- Trend: "Stable" with flat arrow icon (Slate 400)
- Chart: small sparkline showing adoption over last 12 months
- "View all 120 services using .NET" link (blue)
- Divider
- "Override ring placement" dropdown (for org admins)

Warning banner (bottom of page, full width, amber left border):
- Warning icon + "2 technologies in Hold require migration plans" + 
  "View policy compliance" link (blue)
```

---

## Fixing Inconsistencies Between Screens

If screens look different after generation, use these micro-prompts:

**Fix navigation mismatch:**
```
Look at Screen 1 (Navigation Reference). Now fix this screen's top bar 
and sidebar to match Screen 1 EXACTLY — same height, same colors, same 
items, same spacing. Do not change the main content area.
```

**Fix color inconsistency:**
```
The colors on this screen don't match DESIGN.md. Fix: use Slate 900 
(#0F172A) for main background, Slate 800 (#1E293B) for cards and sidebar, 
Slate 700 (#334155) for borders. Primary blue is #2563EB.
```

**Fix typography inconsistency:**
```
Fix the typography on this screen: all UI text should use Inter font. 
H1 = 30px/700, H2 = 24px/600, H3 = 20px/600, body = 14px/400. 
Code or IDs should use JetBrains Mono 13px/400.
```

**Fix spacing issues:**
```
Tighten the spacing to match DESIGN.md: 8px gap between list items, 
16px gap between sections, 24px page padding, 8px card border-radius, 
6px button border-radius.
```

**Match component style:**
```
Fix the cards on this screen to match Screen 2: Slate 800 background, 
1px Slate 700 border, 8px radius, no heavy shadows. Health dots should 
be 8px circles (green #10B981, amber #F59E0B, red #EF4444).
```
