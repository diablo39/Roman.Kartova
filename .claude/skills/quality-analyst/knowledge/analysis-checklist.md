# Analysis Checklist

This checklist is passed to each per-file Explore subagent. The subagent reads one file and performs all sections below.

---

## Instructions for Subagent

Analyze the source file for quality metrics. This is a **READ-ONLY** analysis — do not modify any files.

**Inputs provided by orchestrator:**
- File path to analyze
- Language (detected from extension)
- Changed line ranges (feature-branch mode) or "full file — main mode"

Read the file. Analyze **every** method/function — do not skip any. Analyze methods in order of estimated risk: highest CC first, then security-sensitive, then others. If context runs low, ensure highest-risk methods have full analysis.

---

## 1. Method Identification and Change Status

- Identify all methods/functions with name, start line, end line
- Mark each as `changed` (overlaps changed range), `unchanged`, or `N/A` (main mode)
- Treat property accessors with logic and top-level executable blocks as methods

## 2. Cyclomatic Complexity

Base = 1. Flag HOTSPOT if > 10. Warn "Consider refactoring" if > 50. Methods >100 lines deserve extra scrutiny even if CC is moderate.

**DO count (+1 each):** `if`, `else if`/`elif`, each `case` (incl `default`), `for`/`foreach`/`while`/`do-while`, each `catch`/`except`, each `&&`/`and`, each `||`/`or`, ternary `?:`, null-coalescing `??`

**DON'T count:** bare `else`, `finally`, `return`, `throw`, `break`/`continue`, `lock`/`using`/`await` (unless conditional)

**COMMON MISTAKES:** Don't count bare `else` (no new path). `if (A && B)` = +1 (if) + +1 (&&) = +2 total. Don't miss ternary in LINQ `.Select(x => x > 0 ? a : b)` = +1.

**LINQ Complexity (C#):** Count +1 for each predicate in: `.Where(COND)`, `.Any(COND)`, `.All(COND)`, `.First(COND)`/`.FirstOrDefault(COND)`, `.Count(COND)`, `.Select(x => TERNARY)`. Also count `&&`/`||` inside lambda predicates. DON'T count: `.Select(x => x.Prop)`, `.OrderBy()`, `.GroupBy()`, `.ToList()`/`.ToArray()`. Nested predicates (`.Where(... .Any(...))`) count each predicate separately.

## 3. Nesting Depth

Maximum nesting depth within the method. Method body = depth 0, first `if` = depth 1, etc.

## 4. Decision Points

List each with line number and condition expression.

## 5. Compound Boolean Conditions (MC/DC)

Find every boolean expression with 2+ boolean operators (`&&`/`and`, `||`/`or`).

For each:
- Quote full expression with line number
- Label atomic conditions as A, B, C, etc.
- N = number of atomic conditions, requires N+1 test cases

**Compute MC/DC independence pairs** using Unique-Cause MC/DC (short-circuit masking OK):

Use this canonical truth table format:
```
| # | A | B | C | Decision |
|---|---|---|---|----------|
| T1 | T | T | T | T |
| T2 | F | T | T | F |
```
Independence pairs: A→(T1,T2), B→(T1,T3), ...

Reference patterns:
- `A && B`: {TT, TF, FT} — A→(TT,FT), B→(TT,TF)
- `A || B`: {TF, FT, FF} — A→(TF,FF), B→(FT,FF)
- `A && B && C`: {TTT, FTT, TFT, TTF} — A→(TTT,FTT), B→(TTT,TFT), C→(TTT,TTF)
- Complex: enumerate full truth table, find pairs where only one condition flips and decision changes

## 6. Boundary Operations

Identify comparisons, range checks, and array/collection indexing. Recommend **specific** boundary values:
- `x >= 18` → 17, 18, 19
- `i < arr.Length` → 0, Length-1, Length
- `amount > 0` → -1, 0, 1
- Division → divisor 0, 1, -1

## 7. Error Handling Paths

Identify: try/catch, null checks, guard clauses, validation, Result/Option patterns, timeouts, cancellation, resource disposal. Describe each error scenario needing coverage.

## 8. External Dependencies

Identify: HTTP/gRPC/REST clients, DB operations, file I/O, message queues, caches, third-party SDKs, clock/random. Flag each as integration or mock-based testing needed.

## 9. Concrete Issue Detection

Actively look for these defect patterns — report ONLY when evidence is direct:

**SECURITY**: Hardcoded secrets · SQL injection via string concat · Missing auth on protected endpoint · `DateTime.Now` instead of `UtcNow` in auth/token code · `ex.Message` leaked to client in HTTP responses · `[AllowAnonymous]` on privileged endpoint

**BUG**: Silent exception swallowing (`catch {}` / `catch { return null; }`) · Empty catch block · Off-by-one in loop bounds · Null dereference after incomplete null check · Catch wraps exception but loses inner (`new Exception(msg)` instead of `new Exception(msg, ex)`) · Variable name mismatch in parallel/similar blocks (copy-paste error)

**BUG RISK**: Unused method parameter (ignore interface/override contracts) · `async void` (except event handlers) · `.Value` on nullable without null check · `.First()` without empty-sequence handling · Shared mutable state in parallel tasks (`Task.WhenAll`, `Parallel.ForEach`) without synchronization · `IDisposable` created without `using`/try-finally · `new HttpClient()` per-request (use `IHttpClientFactory`)

**PERFORMANCE**: Identical query/call in loop with same params · Reflection in hot loop without caching · N+1 query pattern · Sync-over-async (`.Result`/`.Wait()` instead of `await`) · DbContext query inside loop (batch/Include instead)

**LOGIC ERROR**: Dead code / unreachable branch · Inverted condition

**DATA LOSS**: Multi-step DB operation without transaction · `SaveChangesAsync()` outside try/catch in multi-entity operations

**FRAGILE**: Magic numbers without constants · Catch base `Exception` without re-throw or specific handling

For each issue: severity, line number, description, evidence.

## 10. Finding Classification (Signal vs Noise)

Classify every finding as:
- **ACTIONABLE**: bugs, security vulnerabilities, performance issues, logic errors, data loss risks — things that need fixing
- **INFORMATIONAL**: dependencies listed, boundaries noted, MC/DC conditions mapped — useful for test planning but not defects

Report counts of each category. The actionable findings ratio = actionable / (actionable + informational).

## 11. Risk Classification

- **HIGH**: money/financial, date/time arithmetic, security/auth/crypto, compliance, CC > 20, 5+ external service/DB calls
- **MEDIUM**: public API surface, 3+ atomic compound conditions, CC 11-20, business rules, state machines, 200+ lines regardless of CC
- **LOW**: simple utility, CC <= 10, no compounds, no boundaries, formatting/logging/config

Highest method risk = file risk. Provide a plain-language reason.

## 12. Recommended Test Focus

Synthesize findings into a **prioritized** bullet list:
1. MC/DC test case sets for compound conditions (highest value)
2. Boundary values from boundary operations (where real defects concentrate)
3. Error scenarios needing coverage (commonly missed)
4. Integration points needing mocks or integration tests
5. Complexity hotspot areas needing thorough path coverage

Each recommendation must be specific enough for a developer or `/test-generator` agent to act on directly.