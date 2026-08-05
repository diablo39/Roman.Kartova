# Observability — SLI-SLO

Section of `knowledge/quality/observability.md`.


The control: service level indicators and objectives turn reliability into an engineering
quantity. An SLI is a ratio of good events to valid events, measured where users experience
the service: availability is non-5xx responses over valid requests; latency is requests faster
than the threshold over valid requests. An SLO sets the target over a window — say 99.9% over
thirty days — and the remainder is the error budget: while budget remains, ship features; when
it burns, reliability work wins the prioritization argument, by prior agreement instead of
by incident-driven negotiation. Code's contribution is the wiring (QUA-045): new user-facing
endpoints register the latency histograms and error counters the SLI queries read, labeled
with the dimensions the SLO dashboards group by — an endpoint invisible to the SLI is
unmonitored by definition, whatever else it logs. Alert on burn rate, not on point failures:
multi-window multi-burn-rate alerts (a fast window catching sudden budget burn, a slow window
catching steady leaks) page when the budget is threatened and stay quiet on blips that will
never breach it. Keep SLO definitions declarative and versioned in the repository where the
platform supports it (OpenSLO-style specs or the observability stack's SLO generators), so
objectives are reviewed changes like everything else.

Verification: in integration or staging, assert the new endpoint's series appear in the set
the SLI query selects; release readiness for a new service includes its SLO, dashboard, and
burn-rate alerts existing (`knowledge/quality/release-readiness.md`); changes to SLO or alert
definitions are reviewed with the same care as the code they judge.
