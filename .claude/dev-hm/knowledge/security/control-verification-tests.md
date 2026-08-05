# Control-verification tests

Every protective control our code provides — an authorization check, a parameterized query, an
enabled certificate verification — is only as durable as the automated test that asserts it.
The three review layers (`knowledge/shared/defense-in-depth.md`) examine a change once; tests
run on every future change. This file defines the mandate that binds the two together, what
makes a control test meaningful, how acceptance criteria trace to tests, and how control tests
stay identifiable and durable over time. The security oracle's control-verification entries
point here. Concrete per-family, per-ecosystem patterns live in
`knowledge/security/control-test-patterns-access.md` (transport, authentication, session,
authorization), `knowledge/security/control-test-patterns-dataflow.md` (parameterized
statements, parser rejection, output encoding, telemetry hygiene, resource limits), and
`knowledge/security/control-test-patterns-browser.md` (protective headers, CSP, request
forgery, cookie and token storage, cache hygiene, subresource integrity). Test-craft
rules — assertions, isolation, determinism, flakiness — live in
`knowledge/quality/test-strategy.md` and apply to control tests unchanged.

## Sections

Read the section you need, not the file. Each row is a separate file.

| Section | File |
|---|---|
| Mandate | `knowledge/security/control-verification-tests/mandate.md` |
| Refusal assertions | `knowledge/security/control-verification-tests/refusal-assertions.md` |
| Fails when removed | `knowledge/security/control-verification-tests/fails-when-removed.md` |
| Durability | `knowledge/security/control-verification-tests/durability.md` |
| Marker convention | `knowledge/security/control-verification-tests/marker-convention.md` |
| Where control tests live in the pyramid | `knowledge/security/control-verification-tests/where-control-tests-live-in-the-pyramid.md` |
| Ownership at the gates | `knowledge/security/control-verification-tests/ownership-at-the-gates.md` |
