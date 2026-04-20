# Kartova — Epics, Features & User Stories

**Source:** [PRODUCT-REQUIREMENTS.md](PRODUCT-REQUIREMENTS.md)
**Date:** 2026-04-16
**Convention:** Epic ID = `E-XX`, Feature ID = `E-XX.F-YY`, Story ID = `E-XX.F-YY.S-ZZ`
**Audit:** Reviewed by Critic agent on 2026-04-16. All CRITICAL and IMPORTANT findings incorporated.

> **Note on PRD Phase 9 reconciliation:** The decomposition intentionally pulls forward several PRD Phase 9 items into earlier phases where they logically belong: interactive dependency graph (-> Phase 1, E-04.F-02), environment tracking (-> Phase 1, E-02.F-05), and scheduled re-scans (-> Phase 2, E-08.F-03). This is a deliberate improvement over the original PRD phasing.

---

## Phase Files

Each phase has been extracted into its own file for focused work:

| Phase | File | Epics | Features | Stories |
|-------|------|-------|----------|---------|
| Phase 0: Foundation | [phase-0-foundation.md](phases/phase-0-foundation.md) | 1 (E-01) | 8 | 33 |
| Phase 1: Core Catalog & Notifications | [phase-1-core-catalog.md](phases/phase-1-core-catalog.md) | 6 (E-02 to E-06, E-06a) | 22 | 55 |
| Phase 2: Auto-Import | [phase-2-auto-import.md](phases/phase-2-auto-import.md) | 4 (E-07 to E-10) | 11 | 36 |
| Phase 3: Documentation | [phase-3-documentation.md](phases/phase-3-documentation.md) | 1 (E-11) | 5 | 15 |
| Phase 4: Status Page | [phase-4-status-page.md](phases/phase-4-status-page.md) | 1 (E-12) | 5 | 16 |
| Phase 5: CLI, Policy & Billing | [phase-5-cli-policy.md](phases/phase-5-cli-policy.md) | 3 (E-13, E-14, E-14a) | 6 | 15 |
| Phase 6: Agent & Monitoring | [phase-6-agent-monitoring.md](phases/phase-6-agent-monitoring.md) | 2 (E-15, E-16) | 6 | 12 |
| Phase 7: Intelligence | [phase-7-intelligence.md](phases/phase-7-intelligence.md) | 4 (E-17 to E-20) | 5 | 13 |
| Phase 8: Analytics | [phase-8-analytics.md](phases/phase-8-analytics.md) | 4 (E-21 to E-24) | 5 | 14 |
| Phase 9: Advanced | [phase-9-advanced.md](phases/phase-9-advanced.md) | 4 (E-25 to E-28) | -- | -- |
| **Total** | | **30** | **73** | **209** |

## Progress Tracking

Track development progress via the checklist: **[CHECKLIST.md](CHECKLIST.md)**

---

## Summary

| Phase | Epics | Features | Stories |
|-------|-------|----------|---------|
| Phase 0: Foundation | 1 (E-01) | 8 | 33 |
| Phase 1: Core Catalog & Notifications | 6 (E-02 to E-06, E-06a) | 22 | 52 |
| Phase 2: Auto-Import | 4 (E-07 to E-10) | 11 | 36 |
| Phase 3: Documentation | 1 (E-11) | 5 | 15 |
| Phase 4: Status Page | 1 (E-12) | 5 | 16 |
| Phase 5: CLI, Policy & Billing | 3 (E-13, E-14, E-14a) | 6 | 15 |
| Phase 6: Agent & Monitoring | 2 (E-15, E-16) | 6 | 12 |
| Phase 7: Intelligence | 4 (E-17 to E-20) | 5 | 13 |
| Phase 8: Analytics | 4 (E-21 to E-24) | 5 | 14 |
| Phase 9: Advanced | 4 (E-25 to E-28) | -- | -- |
| **Total** | **30** | **73** | **206** |

### Audit Trail

| Date | Action |
|------|--------|
| 2026-04-16 | Initial decomposition created |
| 2026-04-16 | Critic audit completed — 9 CRITICAL, 16 IMPORTANT, 6 SUGGESTION findings |
| 2026-04-16 | All CRITICAL and IMPORTANT findings incorporated (see changes below) |
| 2026-04-16 | Split into individual phase files and progress checklist created |

**Key changes from Critic audit:**
- **C-01:** Added E-01.F-06 (Platform API Infrastructure) — versioning, rate limiting, bulk ops, webhooks
- **C-02/C-06/C-07:** Added E-06a (Notification Infrastructure) — dispatch engine, preferences, Slack/Teams
- **C-03:** Added E-12.F-05 (Status Page HA Infrastructure) — separate deployment, data sync, monitoring
- **C-04:** Added SMS to E-12.F-03.S-01 subscriber notifications
- **C-05:** Added E-12.F-01.S-05 (Internal-only status page)
- **C-08:** Added E-01.F-08 (Performance & Scalability Baseline) — indexing strategy, RLS decision
- **C-09:** Added E-08.F-03.S-04 (Conflict review queue for re-scan conflicts)
- **I-01:** Added E-06.F-04 (Environment Map Dashboard), E-06.F-05 (Status Board Dashboard)
- **I-02/I-06:** Added E-01.F-05.S-04 through S-08 (GDPR right to erasure, consent, breach notification, data residency, MiFID II comm records)
- **I-07:** Acknowledged; scan feature split deferred to sprint planning
- **I-09:** Resolved: RLS chosen over schema-per-tenant (E-01.F-08.S-03)
- **I-10:** Split custom domain into domain config + SSL provisioning (E-12.F-01.S-02/S-03)
- **I-13:** Added E-08.F-04 (Scan Resilience & Error Handling) — 4 stories
- **I-14:** Added E-01.F-07 (Platform Observability) — health, logging, metrics, alerting
- **I-16:** Added E-14a (Billing & Subscription Management)
- **I-17:** Reconciliation note added at top of document
- **I-18:** Summary table recounted accurately
- **I-19:** Added E-02.F-01.S-05 (Required minimum field enforcement across all entity types)
- **I-20:** Added E-03.F-05.S-03/S-04 (Multi-ownership permission rules, team deletion handling)
- **I-22:** Note added to E-17.F-01.S-01 about L5 dependency on Phase 8
- **S-04:** Updated E-04.F-01.S-04 with demotion warning semantics
- **S-05:** Updated E-01.F-03.S-03 audit scope to explicitly include relationship changes
- **S-02:** Added E-01.F-06.S-06 (Kartova API self-documentation)
