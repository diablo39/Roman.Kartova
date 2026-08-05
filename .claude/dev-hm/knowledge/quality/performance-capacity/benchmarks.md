# Performance and capacity — Benchmarks

Section of `knowledge/quality/performance-capacity.md`.


The control: claims about speed come from benchmarks with a method, and the method decides how
much to trust the number (QUA-073 makes unmeasured claims mere expectations). Two levels:
micro-benchmarks measure a function or algorithm in isolation using the stack's harness (JMH,
BenchmarkDotNet, criterion, pytest-benchmark or pyperf, Google Benchmark, and their
peers); macro-benchmarks drive a whole request path
with a load tool and measure what users would see. Budgets stated at the API level are checked
by macro-benchmarks; hot inner loops earn micro-benchmarks.

Method over enthusiasm: warm up before measuring (JIT, caches, pools); run enough iterations to
report a distribution, not a single lucky number; fix the inputs and record the environment;
prevent the optimizer from deleting the code under test (use the harness's blackhole/keep-alive
mechanism). A result without its environment and comparison target is a number, not a
measurement.

CI reality: shared runners are noisy. Compare relative to a baseline captured on the same runner
class, apply a noise margin before declaring a regression, and re-run before believing a
surprise. For paths where regressions are expensive, dedicated runners or a
continuous-benchmarking service that tracks results over time and flags statistically
significant changes are worth the setup.

Verification: benchmark code is reviewed like test code — realistic inputs, no
dead-code-elimination artifacts, assertions where the harness supports thresholds. Results in
handoffs carry tool, environment, iteration count, and the baseline compared against.
