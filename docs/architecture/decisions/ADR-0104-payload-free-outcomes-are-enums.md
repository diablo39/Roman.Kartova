# ADR-0104: Payload-Free Operation Outcomes Are Enums, Not Boolean-Flag Result Records

**Status:** Accepted
**Date:** 2026-06-11
**Deciders:** Roman Głogowski (solo developer)
**Category:** Code Conventions
**Related:** ADR-0082 (modular monolith — Application-layer command/handler shape), ADR-0095 (cursor list — result *envelopes* that DO carry payload), ADR-0097 (testing — removes the tautological shape tests this enables)

## Context

Application-layer commands return a value describing which terminal outcome occurred so the endpoint delegate can map it to an HTTP status. Two slice-10 results — `ChangeMemberRoleResult` and `OffboardMemberResult` — were modeled as a `record` of parallel `bool` flags plus static factory properties:

```csharp
public sealed record ChangeMemberRoleResult(bool Changed, bool NotFound, bool InvalidRole, bool LastOrgAdmin)
{
    public static ChangeMemberRoleResult Success      => new(true, false, false, false);
    public static ChangeMemberRoleResult NotFoundResult => new(false, true, false, false);
    // …
}
```

This shape was copied from `AssignApplicationTeamResult`, which carries a **success payload** (the assigned `Application`) — there, a record is justified because the result transports data. The two member-lifecycle results copied the *shape* without the payload that motivates it, and the cost showed up:

- **Illegal states are representable.** The type permits `(true, true, …)` or all-false; only the hand-written factories keep it to the four legal combinations. The compiler enforces nothing.
- **Tautological tests.** Each result needed a "shape test" asserting that factory *X* sets flag *Y* (`ChangeMemberRoleResultTests`, `OffboardMemberResultTests`) — self-referential, no behavioral coverage, exactly the low-value test ADR-0097's assertion-quality bar discourages.
- **No exhaustiveness.** The endpoint `if (result.NotFound) … if (result.LastOrgAdmin) …` chain silently falls through to the success path; a newly-added outcome can go unhandled without a compiler signal.

## Decision

1. **Payload-free, mutually-exclusive operation outcomes are modeled as a C# `enum`** — one member per terminal state — not a boolean-flag record. (`ChangeMemberRoleOutcome`, `OffboardMemberOutcome`.)

2. **Results that carry data on success remain `record`s** (or a discriminated result type). The discriminator is *"does the success case transport a payload?"* — yes → record (e.g. `AssignApplicationTeamResult` returns the app); no → enum.

3. **Endpoint delegates map the outcome with a `switch` expression** that lists every enum member explicitly plus a `_ => throw new InvalidOperationException(...)` guard, so an unmapped future outcome fails loudly (HTTP 500) rather than silently mapping to the success path.

4. Enforcement is by code review + this ADR (not an automated NetArchTest rule — "a record whose properties are all `bool`" is not reliably distinguishable from a legitimate boolean-bearing DTO).

## Consequences

### Positive

- **Illegal states unrepresentable.** An `enum` variable holds exactly one outcome; the `(true, true)` and all-false states cannot occur, with zero hand-written factory guarding.
- **No tautological shape tests.** `ChangeMemberRoleResultTests` and `OffboardMemberResultTests` were deleted — they tested the construction, which no longer exists. Behavioral coverage stays where it belongs: the handler tests assert the returned outcome per branch (`Assert.AreEqual(ChangeMemberRoleOutcome.NotFound, result)`), and integration tests assert the mapped HTTP status.
- **Exhaustive mapping.** The endpoint `switch` enumerates every outcome; the `_ => throw` makes a future unmapped outcome a loud failure.
- **Less code.** The four-bool record + four factories collapses to a four-line enum.

### Negative / Trade-offs

- **The convention is a split, not a single rule:** enum for payload-free outcomes, record for payload-carrying ones. A reviewer must apply the "has success payload?" test. This ADR is the reference that keeps the split principled rather than ad-hoc.
- **The `_ => throw` arm is dead for current code.** C# requires a discard arm on an `enum` `switch` expression (CS8509) even when all members are listed, so the guard is unavoidable; it earns its keep the moment a new outcome is added.
- Not machine-enforced (see Decision 4) — a regression to a boolean-flag result would pass CI; it is caught at review.

### Neutral

- Payload-carrying results are unchanged: `AssignApplicationTeamResult` (returns the assigned application) stays a record. So does anything modeled on ADR-0095 `CursorPage<T>` / result envelopes — those transport data and are out of scope. `DeleteTeamResult` (carries `ApplicationsAssigned`) and `AddTeamMemberResult` (carries `AddedAt`) are likewise exempt — each transports data on a terminal path.
- **The codebase is consistent with this convention.** The slice-8 payload-free records `RemoveTeamMemberResult` / `UpdateTeamMemberResult` were converted to `RemoveTeamMemberOutcome` / `UpdateTeamMemberOutcome` (and their tautological shape tests removed) alongside the member-lifecycle results, so no payload-free boolean-flag result records remain. The exempt payload-carrying records noted above (`AssignApplicationTeamResult`, `DeleteTeamResult`, `AddTeamMemberResult`) are the only result *records* left, each justified by a success payload.

## Alternatives Considered

**Keep boolean-flag records everywhere (rejected).** Uniform with the prior `AssignApplicationTeamResult` convention, but reintroduces representable illegal states and tautological shape tests for every payload-free outcome. The uniformity is superficial — `AssignApplicationTeamResult` is a record for a *reason* (payload) the member-lifecycle results don't share.

**A discriminated-union / `OneOf<>`-style library (rejected).** Would unify payload-free and payload-carrying results under one mechanism, but adds a dependency for a problem a plain `enum` already solves for the payload-free majority; the rare payload-carrying case is adequately served by a record. Not worth the dependency for a solo-maintained codebase.

**A shared generic `Result<TOutcome>` / `Result<T>` type (rejected).** Over-engineering for the current handful of outcomes; obscures the per-operation outcome set behind a generic and complicates the exhaustive `switch`.

## References

- Conversion landed in slice 10: `ChangeMemberRoleCommand.cs` (`ChangeMemberRoleOutcome`), `OffboardMemberCommand.cs` (`OffboardMemberOutcome`); `UserEndpointDelegates` change-role + offboard switches; deleted `ChangeMemberRoleResultTests.cs` / `OffboardMemberResultTests.cs`.
- ADR-0097: Testing taxonomy — the assertion-quality bar that makes the deleted shape tests redundant.
- `AssignApplicationTeamResult` — the retained payload-carrying record that anchors the "has payload?" discriminator.
