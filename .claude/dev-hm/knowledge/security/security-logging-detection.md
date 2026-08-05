# Security logging and detection

The controls that let us tell a protective control was exercised. A denial that leaves no
record cannot be confirmed, counted, or alerted on — which is the failure OWASP names in A09,
Logging and Alerting Failures (edition per `knowledge/shared/versions.md`). Our code emits a
structured event with a stable code at every security decision point, keeps regulated data out
of those events, and ships them somewhere the application cannot rewrite. This file defines
what we record, the code vocabulary, and the tests that assert the deny path emits its event;
the per-diff review entries it backs are SEC-080 – SEC-082 in
`knowledge/security/secure-coding-review.md` (logging section).

## What we record

Every security decision point in our code emits an event when it fires. Denials are recorded
always; successes are recorded where they change security state.

| Decision point | Emitted when |
|---|---|
| Authentication | Every attempt, both outcomes; lockout threshold reached; token issued or revoked |
| Authorization | Every denial — wrong role, foreign object, missing tenant match |
| Input validation | Rejection at a trust boundary (schema violation, unknown field, refused content type) |
| Rate and resource limits | Budget exceeded, oversized input refused, work cap hit (`knowledge/security/resource-protection.md`) |
| Transport | Peer verification failure on an outbound call; refused connection (`knowledge/security/transport-protection.md`) |
| Session lifecycle | Created, expired, revoked, rotated |
| Privilege changes | Role granted or removed, admin action performed |
| Sensitive data | Tier-3 read or export where the classification requires it (`knowledge/security/data-classification.md`) |

Each event carries actor, action, outcome, timestamp, and correlation id. The actor is the
authenticated principal or the explicit value `anonymous` — never a guess.

## Event codes

Every security event carries a stable, machine-readable code alongside its human message.
Detection rules, dashboards, and the verification tests below match on the code; message text
is free to improve, codes are not. A renamed code silently breaks every rule that watched it,
so codes are treated like API contract: additions are cheap, renames go through review, and the
registry lives in one documented place in the repository.

We name codes after the OWASP Application Logging Vocabulary, which gives one shared prefix per
family and portable semantics across services:

| Family | Prefix | Examples |
|---|---|---|
| Authentication | `authn_` | `authn_login_success`, `authn_login_fail`, `authn_login_fail_max`, `authn_token_created`, `authn_token_revoked` |
| Authorization | `authz_` | `authz_fail`, `authz_admin` |
| Input validation | `input_` | `input_validation_fail` |
| Limits and quotas | `excess_` | `excess_rate_limit_exceeded` |
| Session lifecycle | `session_` | `session_created`, `session_expired`, `session_revoked` |
| User management | `user_` | `user_created`, `user_updated`, `user_archived` |
| Privilege changes | `privilege_` | `privilege_permissions_changed` |
| Uploads | `upload_` | `upload_complete`, `upload_validation` |
| Sensitive data | `sensitive_` | `sensitive_read`, `sensitive_export` |
| System and config | `sys_` | `sys_startup`, `sys_monitor_disabled` |

A control family without a code cannot be confirmed in production: when a new protective
control is added, its refusal code is named in the same diff, and the control-verification test
asserts it (below). This is the anchor the oracle's stable-event-code entry points at.

## Structured events

One structured record per event, through the project logger — never string interpolation into a
message line. External input appears only as field values, so a value containing line breaks or
control characters cannot forge a second record (SEC-081).

```json
{ "datetime": "2026-07-13T10:41:07Z", "appid": "orders-api", "event": "authz_fail:u-7841,orders/9313",
  "level": "warn", "description": "user attempted to read another principal's order",
  "actor": "u-7841", "source_ip": "203.0.113.40", "request_method": "GET",
  "request_uri": "/orders/9313", "correlation_id": "01JZX..." }
```

Timestamps are UTC in ISO 8601. The correlation id is the same one the error envelope returns
to the caller (`knowledge/security/api-surface.md`, error shape), so a support report, the
response, and the event line join on one key.

## Severity and alerting

Levels follow one convention so "alertable" means the same thing in every service:

- `info` — expected security activity: a single failed login, a single validation rejection.
  Counted, not alerted.
- `warn` — a threshold or pattern worth a look: lockout reached (`authn_login_fail_max`), rate
  limit tripped, repeated authorization denials from one principal.
- `error` / `critical` — someone should respond: peer verification failing on an internal
  call, monitoring or a protective control found disabled (`sys_monitor_disabled`), tamper
  indicators.

Application code emits facts at the conventional level; thresholding and aggregation
("five `authn_login_fail` for one account within a minute") belong to the detection layer, not
to scattered counters in handlers. Every code at `warn` and above has a named consumer — an
alert rule or a dashboard someone owns. An event nobody can act on fails A09's purpose exactly
as if it were never logged; reviewing the code-to-rule mapping is part of adding the event.

## Regulated data stays out

Security events describe that something happened, never the sensitive value it happened with.
No Tier-3 data — credentials, tokens, session identifiers, personal, payment, or health values
— appears in any event field (`knowledge/security/data-classification/telemetry.md`, SEC-082).
Concretely: identifiers are masked or tokenized; session ids are logged as a short hash prefix,
enough to correlate, not enough to replay; validation events name the offending field and
constraint but never echo the submitted value, mirroring the error-shape rule in
`knowledge/security/api-surface.md`; whole request or response objects are never serialized
into an event, because headers and bodies carry credentials.

## Correlation across services

A correlation id is minted at the edge and propagated on every internal call per the project's
trace-context convention (`knowledge/quality/observability/propagation.md`); every security
event includes it. All services use the same code vocabulary, so one detection rule spans the
fleet — an `authz_fail` means the same thing wherever it fires. Clocks are NTP-disciplined and
events carry UTC timestamps, so cross-service sequences reconstruct in order.

## Tamper evidence

The audit value of a security event depends on the application not being able to unwrite it.
Events ship promptly to a centralized store; the application's own principal can append but not
update or delete (write-only credentials); retention on the store follows the project's
declared policy, not the container's disk lifetime. This is the enforced check SEC-186: a diff
that adds or changes security-event shipping passes only with append-only credentials or the
store's immutability feature enabled, cited in the handoff. Local files and stdout are a buffer on the
way out, never the system of record. Where the platform store offers integrity features —
immutability windows, server-side hashing — the configuration enabling them is cited in the
handoff. Emission is fail-safe in both directions: a logging outage never turns a denial into
an approval (fail closed belongs to the control, SEC-071), and a control that denies while its
event is dropped emits a degraded-logging system event as soon as the pipeline recovers.

## Verification tests

The deny path emits its event — that is the assertion, and it rides the same control tests
mandated by `knowledge/security/control-verification-tests.md`:

- Each refusal test (wrong role, forged request, oversized input, failed login) captures the
  log stream in the harness and asserts exactly one event with the expected code and outcome
  fields, alongside the refused response itself.
- A telemetry-hygiene test drives a flow with marker values in Tier-3 positions (a fake
  password, token, and personal identifier) and asserts none of the markers appear anywhere in
  the captured output (pattern in `knowledge/security/control-test-patterns-dataflow.md`).
- A log-injection test submits input containing line breaks and control characters and asserts
  the captured stream still parses as the same number of structured records.
- The event-code registry has a snapshot test, so a renamed or deleted code is a reviewed diff,
  not a silently broken detection rule.
