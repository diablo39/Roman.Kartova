# Performance and capacity — Capacity

Section of `knowledge/quality/performance-capacity.md`.


The control: capacity limits are explicit in code and configuration, because a limit the code
doesn't enforce is enforced later by an outage. Bounded connection and thread pools, bounded
queues with a stated rejection policy, request-concurrency caps, autoscaling floors and
ceilings — QUA-072 already refuses unbounded caches, queues, and buffers at introduction; this
section is about choosing the bounds. The per-dependency compartments the bounds create —
bulkheads, and the breakers that trip when they saturate — live in
`knowledge/quality/reliability-resilience.md#breakers`. Size from arithmetic, not defaults: Little's law
(concurrent work = arrival rate × service time) sizes pools and worker counts from measured
latency and expected throughput; tolerable wait times size queue depths. Defaults shipped by
libraries were sized for someone else's system.

Plan headroom deliberately: run at a stated fraction of measured capacity — half to two-thirds
is a common operating point — so traffic bursts, deploys, and the loss of an instance or zone
don't breach the latency budget. The stress-test knee from the previous section is the
denominator of that fraction; without it, headroom claims are decoration. Prefer backpressure to
buffering: when a stage saturates, slow or refuse the producer (bounded queue, credit or window
schemes) instead of absorbing unbounded work that turns into a memory incident with a delay
timer. Watch saturation, not just latency — utilization and saturation metrics (the USE reading
of resources, `knowledge/quality/observability.md#metrics`) show the wall before you hit it.

Verification: limits appear in config with units, values, and a line of rationale (QUA-051
documents them); a test drives the system to a limit and asserts the designed refusal or
backpressure behavior — a quick rejection, not an out-of-memory; capacity and headroom claims in
handoffs cite the load-test knee they derive from.
