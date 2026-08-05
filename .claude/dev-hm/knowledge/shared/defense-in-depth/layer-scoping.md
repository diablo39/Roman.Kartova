# Layer scoping — what each layer re-runs

The layers are independent lenses, not repetitions. Independence is preserved where it changes
outcomes and dropped where it only re-derives an agreed, evidenced fact.

| Layer | Section routing | Entry evaluation |
|---|---|---|
| 1 Self-check | Routes off the index against its own diff | Every entry in every activated section, from the code |
| 2 Verification | Routes independently — does **not** read layer 1's section list before deciding | Every entry in every activated section, from the code. Never inherits a layer-1 verdict |
| 3 Gate | Routes independently, then compares its section set against layers 1–2; a section they activated and it did not (or the reverse) is itself a finding | **Every S0 and S1 entry** in its activated sections, re-derived from the code. For S2/S3 entries where layers 1 and 2 recorded *matching* verdicts with evidence, the gate verifies the recorded evidence supports the verdict instead of re-deriving it, and re-derives a spot-check sample of at least three. Any layer-1/layer-2 **disagreement** is re-run in full and is a finding in itself |

The gate's blocking authority is unchanged: everything that can fail a work item — S0, S1, waiver
adjudication, control-verification tests (SEC-130 – SEC-134) — is re-derived from the code by the
gate itself, never inherited. What the gate stops doing is re-deriving S2/S3 entries that two
prior independent runs already agreed on and evidenced.

A layer that did not receive the prior layers' verdict tables (skipped layer, direct invocation)
re-derives everything and says so in its scope statement.

### Handoff artifact

Layers 1 and 2 emit their verdict table as a fenced `oracle-verdicts` block so the next layer can
scope without re-reading prose:

```oracle-verdicts
layer: 2
sections: SEC[injection, authn-authz, secrets, session-lifecycle, control-tests] QUA[correctness, maintainability, test-adequacy, error-handling]
SEC core 24 pass, 64 n/a; SEC-CS 6 pass, 2 n/a; QUA core 19 pass, 1 fail, 41 n/a
QUA-004 fail src/OrderService.cs:52 — no test covers the empty-cart branch
SEC-062 waive-requested package.json — advisory in a dev-only transitive dep
finding S2 src/OrderService.cs:14 — TODO references a closed ticket
```

The `sections:` line is the routing record. The gate reads it *after* routing on its own, to
compare — not to decide.

The three review layers run once per change; the control-verification tests they leave behind run
on every future change. They convert a gate-time assurance — the control was present when
reviewed — into a regression-time assurance: a change that weakens the control fails the build.
The gates verify this layer exists and bites rather than substituting for it.
