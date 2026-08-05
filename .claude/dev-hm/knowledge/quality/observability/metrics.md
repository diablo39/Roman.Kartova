# Observability — Metrics

Section of `knowledge/quality/observability.md`.


The control: metrics answer how much and how often, cheaply enough to keep forever and
aggregate fleet-wide. Instrument every service with the RED set — rate, errors, and duration
per operation — and every resource with the USE set — utilization, saturation, errors; together
they cover the four golden signals of latency, traffic, errors, and saturation. Conventions
make metrics dashboard-ready: follow the repository's naming plus the OpenTelemetry semantic
conventions; state units; record durations as histograms so percentiles are computed at query
time (exponential or native histogram types where the backend supports them), never as
pre-averaged gauges; use counters for events and gauges for current states. Cardinality is the
budget: labels hold only low-cardinality dimensions (operation, status class, region) — a user
ID or raw URL in a label multiplies time series until the metrics store falls over; that detail
belongs in traces and logs. Exemplars link histogram samples to the traces behind them, which
turns "p99 got worse" into "here are the slow requests".

Verification: a metric-emission test exercises the path and asserts the expected series with
the expected labels exist in the registry or test exporter; review checks that new endpoints
register the standard RED instruments (QUA-042, and the SLI wiring below); any label fed from
unbounded values is a finding.
