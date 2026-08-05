# API surface protection — Inventory

Section of `knowledge/security/api-surface.md`.


Every exposed route is intentional and enumerable (API9). The route table is generated from code
into a spec, or a spec exists and a test compares the running route table against it — either
way, adding an endpoint without touching a reviewed artifact is impossible. Debug, profiling,
and framework-introspection endpoints are absent from production builds or gated behind the
platform's operator authentication, stated explicitly in configuration. Deprecated API versions
carry a retirement date and are removed on it — an old version nobody watches is the softest
door on the surface. Non-production environments do not hold production data behind weaker
controls; if a staging instance is internet-reachable, it meets the same bar.

Verification tests: a route-snapshot test fails when an endpoint appears that the checked-in
spec does not list, forcing the spec change through review; a production-configuration test
asserts the debug-endpoint switches are off.

Every control above is a protective control in the sense of
`knowledge/security/control-verification-tests.md`: its test must fail when the control is
removed, and refusals should emit their security event
(`knowledge/security/security-logging-detection.md#event-codes`).
