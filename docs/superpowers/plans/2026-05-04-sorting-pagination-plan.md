# Sorting & Cursor Pagination Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship a reusable cursor-pagination + sort contract (API + frontend primitives), apply it as the reference implementation on `GET /api/v1/catalog/applications`, and enforce "every list paginates from first cut" via an architecture fitness test + new ADR-0095.

**Architecture:** Backend factors a `IQueryable<T>.ToCursorPagedAsync(...)` extension in `Kartova.SharedKernel/Pagination/`; handlers compose filters + projection and call the extension at the tail. Wire envelope is `CursorPage<T> { items, nextCursor, prevCursor }` with opaque base64url JSON cursor `{ s, i, d }`. Sort is `?sortBy=...&sortOrder=asc|desc` with per-resource enum allowlist. Frontend wraps TanStack `useInfiniteQuery` in a generic `useCursorList` hook + `useListUrlState` (URL holds sort, cursor is ephemeral) and surfaces a `<DataTable>` shell using Untitled UI primitives.

**Tech Stack:** .NET 10 + EF Core + PostgreSQL 16 (RLS) + xUnit + Testcontainers · React + TypeScript + TanStack Query + react-aria-components (Untitled UI) + React Router + Vitest + Playwright MCP.

**Path deviation from spec:** the spec uses `src/SharedKernel/Kartova.SharedKernel.Contracts/...` but the real repo layout is flat: `src/Kartova.SharedKernel/` with no nested folder and no separate `Kartova.SharedKernel.Contracts` project. Pure-data carriers in SharedKernel get `[ExcludeFromCodeCoverage]` applied manually (matching `DomainEvent.cs`, `IModule.cs`). All file paths in this plan reflect the real layout.

**ADR number:** ADR-0095 (the new pagination ADR). ADR-0094 was assigned to the renumbered Untitled UI ADR in commit `130c6f1` to fix a 0092 collision. Spec is at `docs/superpowers/specs/2026-05-04-sorting-pagination-design.md`.

**Slice scope reminder:** the new convention applies to the `Applications` list endpoint here — but the SharedKernel pieces, the architecture fitness rule, and the ADR are reusable for every future list endpoint (Components / Services / Libraries in E-02, etc.).

---

## File Structure

### Backend — new files

```
src/Kartova.SharedKernel/Pagination/
  CursorPage.cs                      # CursorPage<T> record (wire envelope)
  SortOrder.cs                       # enum { Asc, Desc }
  BoundedListResultAttribute.cs      # marker — exempt handler from arch rule
  CursorCodec.cs                     # static base64url JSON encode/decode
  SortSpec.cs                        # SortSpec<TEntity>(name, keySelector)
  InvalidSortFieldException.cs       # carries field + allowed list
  InvalidCursorException.cs
  QueryablePagingExtensions.cs       # ToCursorPagedAsync<T>(...)

src/Kartova.SharedKernel.AspNetCore/
  PagingExceptionHandler.cs          # exceptions → RFC 7807 400

src/Modules/Catalog/Kartova.Catalog.Contracts/
  ApplicationSortField.cs            # enum { CreatedAt, Name }

src/Modules/Catalog/Kartova.Catalog.Infrastructure/
  ApplicationSortSpecs.cs            # static SortSpec<Application> instances

tests/Kartova.SharedKernel.Tests/Pagination/
  CursorCodecTests.cs
  QueryablePagingExtensionsTests.cs
  SortSpecTests.cs

tests/Kartova.SharedKernel.AspNetCore.Tests/
  PagingExceptionHandlerTests.cs

tests/Kartova.Catalog.IntegrationTests/        # NEW project (mirrors Api integration tests structure)
  Kartova.Catalog.IntegrationTests.csproj
  ListApplicationsPaginationTests.cs

tests/Kartova.ArchitectureTests/
  PaginationConventionRules.cs

docs/architecture/decisions/
  ADR-0095-cursor-pagination-contract.md
```

### Backend — modified files

```
src/Modules/Catalog/Kartova.Catalog.Application/ListApplicationsQuery.cs
src/Modules/Catalog/Kartova.Catalog.Infrastructure/ListApplicationsHandler.cs
src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogEndpointDelegates.cs
src/Kartova.Migrator/DevSeed.cs                # bump from 1 org → 1 org + 120 apps
src/Kartova.Api/Program.cs                     # register PagingExceptionHandler
docs/architecture/decisions/README.md          # index + chronology entry
docs/architecture/decisions/ADR-0029-rest-as-primary-api-style.md  # cross-link to ADR-0095
CLAUDE.md                                      # working-agreement bullet
docs/product/CHECKLIST.md                      # tick the new convention milestone
```

### Frontend — new files

```
web/src/lib/list/
  types.ts                           # SortDirection, CursorListResult<T>
  useCursorList.ts                   # TanStack useInfiniteQuery wrapper
  useListUrlState.ts                 # useSearchParams wrapper

web/src/components/application/data-table/
  data-table.tsx                     # <DataTable>, <SortableHead>, <TablePager>, <TableSkeleton>
  __tests__/sortable-head.test.tsx
  __tests__/table-pager.test.tsx

web/src/lib/list/__tests__/
  use-cursor-list.test.tsx
  use-list-url-state.test.tsx
```

### Frontend — modified files

```
web/src/features/catalog/api/applications.ts
web/src/features/catalog/components/ApplicationsTable.tsx
web/src/features/catalog/pages/CatalogListPage.tsx
web/src/api/openapi/schema.d.ts (or wherever generated types live — regenerate)
```

---

## Phase A — ADR + working-agreement scaffold

### Task 1: Author ADR-0095 (Proposed) + CLAUDE.md working agreement

**Why first:** locks the convention before any code lands; gives every later task a stable "per ADR-0095" reference; keeps the slice greppable.

**Files:**
- Create: `docs/architecture/decisions/ADR-0095-cursor-pagination-contract.md`
- Modify: `CLAUDE.md` (Working agreements section)
- Modify: `docs/architecture/decisions/README.md` (index row + chronology)

- [ ] **Step 1: Create ADR-0095 stub**

Create `docs/architecture/decisions/ADR-0095-cursor-pagination-contract.md`:

```markdown
# ADR-0095: Cursor Pagination Contract — Wire Shape, Sort Syntax, and First-Cut Mandate

**Status:** Proposed
**Date:** 2026-05-04
**Deciders:** Roman Głogowski (solo developer)
**Category:** API & Integration Architecture
**Related:** ADR-0029 (REST as primary API style — refines its "pagination via cursors" mention), ADR-0083 (test pyramid), ADR-0090 (tenant scope), ADR-0091 (RFC 7807 error responses), ADR-0092 (REST URL convention).

## Context

ADR-0029 stated "pagination via cursors" as policy without a concrete contract. The Catalog `Applications` list endpoint is the first list shipped; without freezing the wire shape now, every later list endpoint (Components, Services, Libraries in E-02; logs and audit trails in later phases) re-debates it.

We also observed that adding pagination retroactively is a wire-shape break + a UI rework — both expensive — so the policy here covers not just *how* lists paginate, but *that* every list endpoint and screen does so from the first cut.

## Decision

1. **Wire envelope.** Every list endpoint returns `CursorPage<T> { items: T[], nextCursor: string | null, prevCursor: string | null }`.
2. **Sort syntax.** Two query params: `?sortBy=<field>&sortOrder=<asc|desc>`. `sortBy` is a per-resource enum surfaced by OpenAPI; the server enforces an allowlist. Single-field sort only in MVP.
3. **Cursor format.** Opaque base64url-encoded JSON `{ s, i, d }` — sort value, id (tiebreaker), direction. Format is internal; clients MUST treat the cursor as opaque.
4. **Pagination style.** Pure cursor; no `total`, no `page`, no `hasMore` (derivable from `nextCursor`). No `?include=total` opt-in in MVP.
5. **`prevCursor`.** Reserved on the wire; always `null` in MVP. Frontend manages "Prev" via a client-side cursor stack.
6. **Limit.** `?limit=N`, default 50, max 200, range error → 400 RFC 7807.
7. **Error type prefixes** (per ADR-0091): `https://kartova.dev/problems/invalid-sort-field`, `.../invalid-sort-order`, `.../invalid-cursor`, `.../invalid-limit`.
8. **Standing convention.** Every new list endpoint and every new list screen MUST be designed and implemented with sorting + cursor pagination from the first cut. Bounded lists (≤ N rows by domain invariant) MAY return a flat array, but MUST be decorated with `[BoundedListResult]` and an inline justification comment citing the cap. Default is paginated; opt-out is explicit.
9. **Architecture fitness test** enforces clause 8 — `tests/Kartova.ArchitectureTests/PaginationConventionRules.cs`.

## Consequences

- One reusable extension method (`IQueryable<T>.ToCursorPagedAsync`) carries the keyset filter, the `+1` trick, and the cursor codec. Handlers compose filters and call the extension at the tail.
- OpenAPI generates per-resource sort-field enums (`SortByApplications`, `SortByComponents`, …); the frontend gets compile-time-safe sort values.
- "Jump to page N" is impossible by construction. For screens that genuinely need it (admin moderation, audit trails), `[BoundedListResult]` on a separate endpoint is the escape hatch.
- Cursors are time-bound; sharing cursors across a sort change or a long-lived bookmark is brittle. The `d` field in the cursor JSON guards against direction-mismatched reuse → 400.

## Implementation notes

- `s` (sort value) carries a JSON scalar (string, number, or ISO-8601 timestamp string).
- Stable tiebreaker is `id` (Guid). The keyset filter uses PostgreSQL row-constructor comparison: `(sortKey, id) > (?, ?)` for `asc`, reversed for `desc`. EF Core's PostgreSQL provider translates this directly.
- Cursor decode mismatching `d` against the request's `sortOrder` throws `InvalidCursorException` → 400.
- `gcTime` on frontend `useCursorList` set to 5 min default to bound `useInfiniteQuery` cache growth.
```

- [ ] **Step 2: Add CLAUDE.md working agreement**

In `CLAUDE.md`, in the "Working agreements" section, add:

```markdown
- **List endpoints & list screens:** every new list endpoint exposes `sortBy` / `sortOrder` / `cursor` / `limit` and returns `CursorPage<T>` (ADR-0095). Every new list screen wires `useCursorList` + `useListUrlState` + `<DataTable>`. Treat as part of "first cut" — not a follow-up phase. Bounded lists may return flat arrays only when decorated with `[BoundedListResult]` + inline justification.
```

Insert after the existing "Cross-module interactions" bullet, before "When proposing new ADRs".

- [ ] **Step 3: Update ADR README index + chronology**

In `docs/architecture/decisions/README.md`, append a row to the keyword index after ADR-0094:

```markdown
| [0095](ADR-0095-cursor-pagination-contract.md) | Cursor Pagination Contract — Wire Shape, Sort Syntax, and First-Cut Mandate | API & Integration Architecture | Proposed | 0029, 0083, 0090, 0091, 0092 | List endpoints return `CursorPage<T>` envelope with opaque base64url cursor `{s,i,d}`; `?sortBy=<field>&sortOrder=asc\|desc` per-resource enum allowlist; default 50, max 200; pure cursor (no total). First-cut mandate enforced by `PaginationConventionRules` arch test; `[BoundedListResult]` opt-out for bounded lists. |
```

Append to the chronology table after the 2026-05-01 entry:

```markdown
| 2026-05-04 | ADR-0095 (Cursor pagination contract) proposed — concrete contract for ADR-0029's "pagination via cursors" mention; first-cut mandate + arch fitness rule; reference impl on Applications list |
```

- [ ] **Step 4: Build green check**

Run: `cmd //c "dotnet build Kartova.slnx --tl:off"`

Expected: 0 warnings, 0 errors. (Doc-only change — should not affect build.)

- [ ] **Step 5: Commit**

```bash
git add docs/architecture/decisions/ADR-0095-cursor-pagination-contract.md \
        docs/architecture/decisions/README.md \
        CLAUDE.md
git commit -m "docs(adr): ADR-0095 cursor pagination contract (Proposed) + CLAUDE.md working agreement"
```

---

## Phase B — SharedKernel pagination primitives

### Task 2: CursorPage<T>, SortOrder, BoundedListResultAttribute (pure carriers)

**Files:**
- Create: `src/Kartova.SharedKernel/Pagination/CursorPage.cs`
- Create: `src/Kartova.SharedKernel/Pagination/SortOrder.cs`
- Create: `src/Kartova.SharedKernel/Pagination/BoundedListResultAttribute.cs`

- [ ] **Step 1: Create `CursorPage.cs`**

```csharp
using System.Diagnostics.CodeAnalysis;

namespace Kartova.SharedKernel.Pagination;

/// <summary>
/// Standard wire envelope for every paginated list endpoint (ADR-0095).
/// <para>
/// <c>NextCursor</c> is null on the last page; clients MUST treat the value as opaque.
/// <c>PrevCursor</c> is reserved on the wire but always null in MVP — the frontend
/// manages prev navigation via a client-side cursor stack.
/// </para>
/// </summary>
[ExcludeFromCodeCoverage]
public sealed record CursorPage<T>(
    IReadOnlyList<T> Items,
    string? NextCursor,
    string? PrevCursor);
```

- [ ] **Step 2: Create `SortOrder.cs`**

```csharp
using System.Diagnostics.CodeAnalysis;

namespace Kartova.SharedKernel.Pagination;

/// <summary>
/// Direction component of <c>?sortOrder=asc|desc</c> (ADR-0095).
/// </summary>
[ExcludeFromCodeCoverage]
public enum SortOrder
{
    Asc,
    Desc
}
```

- [ ] **Step 3: Create `BoundedListResultAttribute.cs`**

```csharp
using System.Diagnostics.CodeAnalysis;

namespace Kartova.SharedKernel.Pagination;

/// <summary>
/// Marks a <c>List*Handler</c> as exempt from the cursor-pagination
/// fitness rule (<c>PaginationConventionRules</c>) because the result set
/// is bounded by domain invariant. The exemption MUST be justified inline
/// in the handler with a comment citing the cap. ADR-0095 §8.
/// </summary>
[ExcludeFromCodeCoverage]
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class BoundedListResultAttribute : Attribute
{
    public string Reason { get; }
    public BoundedListResultAttribute(string reason) => Reason = reason;
}
```

- [ ] **Step 4: Build green check**

Run: `cmd //c "dotnet build src/Kartova.SharedKernel/Kartova.SharedKernel.csproj --tl:off"`
Expected: 0 warnings, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/Kartova.SharedKernel/Pagination/CursorPage.cs \
        src/Kartova.SharedKernel/Pagination/SortOrder.cs \
        src/Kartova.SharedKernel/Pagination/BoundedListResultAttribute.cs
git commit -m "feat(sharedkernel): CursorPage<T>, SortOrder, BoundedListResultAttribute (ADR-0095)"
```

---

### Task 3: CursorCodec (TDD)

**Files:**
- Create: `src/Kartova.SharedKernel/Pagination/CursorCodec.cs`
- Create: `tests/Kartova.SharedKernel.Tests/Pagination/CursorCodecTests.cs`

- [ ] **Step 1: Write failing tests for `CursorCodec`**

Create `tests/Kartova.SharedKernel.Tests/Pagination/CursorCodecTests.cs`:

```csharp
using FluentAssertions;
using Kartova.SharedKernel.Pagination;

namespace Kartova.SharedKernel.Tests.Pagination;

public sealed class CursorCodecTests
{
    private static readonly Guid AnyId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    [Fact]
    public void Encode_then_Decode_roundtrips_string_sort_value()
    {
        var encoded = CursorCodec.Encode("alpha", AnyId, SortOrder.Asc);
        var decoded = CursorCodec.Decode(encoded);

        decoded.SortValue.Should().Be("alpha");
        decoded.Id.Should().Be(AnyId);
        decoded.Direction.Should().Be(SortOrder.Asc);
    }

    [Fact]
    public void Encode_then_Decode_roundtrips_iso8601_timestamp_string()
    {
        var encoded = CursorCodec.Encode("2026-05-04T12:34:56.789Z", AnyId, SortOrder.Desc);
        var decoded = CursorCodec.Decode(encoded);

        decoded.SortValue.Should().Be("2026-05-04T12:34:56.789Z");
        decoded.Direction.Should().Be(SortOrder.Desc);
    }

    [Fact]
    public void Encode_produces_url_safe_string()
    {
        // base64url: no '+' '/' '='
        var encoded = CursorCodec.Encode("value with spaces & symbols/+", AnyId, SortOrder.Asc);

        encoded.Should().NotContain("+");
        encoded.Should().NotContain("/");
        encoded.Should().NotContain("=");
    }

    [Fact]
    public void Decode_throws_InvalidCursorException_on_garbage_input()
    {
        var act = () => CursorCodec.Decode("not-a-valid-cursor!!!");

        act.Should().Throw<InvalidCursorException>();
    }

    [Fact]
    public void Decode_throws_InvalidCursorException_on_tampered_base64()
    {
        var encoded = CursorCodec.Encode("alpha", AnyId, SortOrder.Asc);
        // Flip one character in the middle to corrupt the JSON payload.
        var tampered = encoded[..(encoded.Length / 2)] + "X" + encoded[(encoded.Length / 2 + 1)..];

        var act = () => CursorCodec.Decode(tampered);

        act.Should().Throw<InvalidCursorException>();
    }

    [Fact]
    public void Decode_throws_InvalidCursorException_when_required_field_missing()
    {
        // base64url-encoded `{"s":"alpha"}` (missing i and d)
        var malformed = Convert.ToBase64String("{\"s\":\"alpha\"}"u8.ToArray())
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var act = () => CursorCodec.Decode(malformed);

        act.Should().Throw<InvalidCursorException>();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cmd //c "dotnet test tests/Kartova.SharedKernel.Tests/Kartova.SharedKernel.Tests.csproj --filter FullyQualifiedName~CursorCodecTests --tl:off"`
Expected: compilation FAIL ("CursorCodec does not exist", "InvalidCursorException does not exist").

- [ ] **Step 3: Create `CursorCodec.cs`**

```csharp
using System.Buffers.Text;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kartova.SharedKernel.Pagination;

/// <summary>
/// Encodes and decodes opaque pagination cursors per ADR-0095.
/// Wire format is base64url-encoded JSON `{ s, i, d }`:
/// <list type="bullet">
/// <item><description><c>s</c> — sort value of the boundary row (string|number|ISO-8601 string)</description></item>
/// <item><description><c>i</c> — boundary row id (Guid, tiebreaker)</description></item>
/// <item><description><c>d</c> — direction the cursor was produced under ("asc"|"desc"). The handler verifies this matches the request's <c>sortOrder</c> to detect reused cursors across a sort flip.</description></item>
/// </list>
/// </summary>
public static class CursorCodec
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = false,
    };

    public sealed record DecodedCursor(object SortValue, Guid Id, SortOrder Direction);

    public static string Encode(object sortValue, Guid id, SortOrder direction)
    {
        var payload = new CursorPayload(sortValue, id, direction == SortOrder.Asc ? "asc" : "desc");
        var json = JsonSerializer.SerializeToUtf8Bytes(payload, Options);
        return ToBase64Url(json);
    }

    public static DecodedCursor Decode(string cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            throw new InvalidCursorException("Cursor is empty.");
        }

        byte[] bytes;
        try
        {
            bytes = FromBase64Url(cursor);
        }
        catch (FormatException ex)
        {
            throw new InvalidCursorException("Cursor is not valid base64url.", ex);
        }

        CursorPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<CursorPayload>(bytes, Options);
        }
        catch (JsonException ex)
        {
            throw new InvalidCursorException("Cursor JSON is malformed.", ex);
        }

        if (payload is null
            || payload.S is null
            || payload.I == Guid.Empty
            || payload.D is not "asc" and not "desc")
        {
            throw new InvalidCursorException("Cursor is missing required fields.");
        }

        var direction = payload.D == "asc" ? SortOrder.Asc : SortOrder.Desc;
        // System.Text.Json deserializes JsonElement for `object` — unwrap to the underlying scalar.
        var sortValue = payload.S is JsonElement el ? UnwrapJsonElement(el) : payload.S;
        return new DecodedCursor(sortValue, payload.I, direction);
    }

    private static object UnwrapJsonElement(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String => el.GetString()!,
        JsonValueKind.Number => el.TryGetInt64(out var i) ? i : el.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => throw new InvalidCursorException($"Unsupported cursor sort-value kind: {el.ValueKind}"),
    };

    private static string ToBase64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string s)
    {
        var b64 = s.Replace('-', '+').Replace('_', '/');
        switch (b64.Length % 4)
        {
            case 2: b64 += "=="; break;
            case 3: b64 += "="; break;
            case 1: throw new FormatException("Invalid base64url length.");
        }
        return Convert.FromBase64String(b64);
    }

    private sealed record CursorPayload(
        [property: JsonPropertyName("s")] object? S,
        [property: JsonPropertyName("i")] Guid I,
        [property: JsonPropertyName("d")] string? D);
}
```

Note: `InvalidCursorException` does not yet exist — Task 4 creates it. Codec tests will continue to fail compilation until then; that is expected. Hold the build error until end of Task 4.

- [ ] **Step 4: Defer build verification to Task 4**

Skip build/test runs for now — `InvalidCursorException` is required and is created in Task 4.

- [ ] **Step 5: No commit yet — combine with Task 4**

Hold the diff; commit happens at end of Task 4 alongside `InvalidCursorException` and `SortSpec`.

---

### Task 4: SortSpec, exceptions

**Files:**
- Create: `src/Kartova.SharedKernel/Pagination/SortSpec.cs`
- Create: `src/Kartova.SharedKernel/Pagination/InvalidSortFieldException.cs`
- Create: `src/Kartova.SharedKernel/Pagination/InvalidCursorException.cs`
- Create: `tests/Kartova.SharedKernel.Tests/Pagination/SortSpecTests.cs`

- [ ] **Step 1: Create `InvalidCursorException.cs`**

```csharp
using System.Diagnostics.CodeAnalysis;

namespace Kartova.SharedKernel.Pagination;

/// <summary>
/// Thrown when a cursor cannot be decoded, has been tampered with, is missing
/// required fields, or its embedded direction does not match the current
/// request's <c>sortOrder</c>. Mapped to RFC 7807 400 by
/// <c>PagingExceptionHandler</c>. ADR-0095 §4.3.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed class InvalidCursorException : Exception
{
    public InvalidCursorException(string message) : base(message) { }
    public InvalidCursorException(string message, Exception inner) : base(message, inner) { }
}
```

- [ ] **Step 2: Create `InvalidSortFieldException.cs`**

```csharp
using System.Diagnostics.CodeAnalysis;

namespace Kartova.SharedKernel.Pagination;

/// <summary>
/// Thrown when <c>?sortBy</c> falls outside the per-resource allowlist.
/// Mapped to RFC 7807 400 with <c>allowedFields</c> in the response.
/// ADR-0095 §4.3.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed class InvalidSortFieldException : Exception
{
    public string FieldName { get; }
    public IReadOnlyList<string> AllowedFields { get; }

    public InvalidSortFieldException(string fieldName, IReadOnlyList<string> allowedFields)
        : base($"Sort field '{fieldName}' is not allowed. Allowed: {string.Join(", ", allowedFields)}.")
    {
        FieldName = fieldName;
        AllowedFields = allowedFields;
    }
}
```

- [ ] **Step 3: Create `SortSpec.cs`**

```csharp
using System.Linq.Expressions;

namespace Kartova.SharedKernel.Pagination;

/// <summary>
/// Describes one sortable field for a list query: the public field name
/// (matches OpenAPI enum value) and the EF Core key selector. Per-resource
/// allowlists are expressed as collections of <c>SortSpec&lt;TEntity&gt;</c>
/// instances co-located with the handler that enforces them. ADR-0095 §5.
/// </summary>
public sealed record SortSpec<TEntity>(
    string FieldName,
    Expression<Func<TEntity, object>> KeySelector);
```

- [ ] **Step 4: Write tests for `SortSpec` construction**

Create `tests/Kartova.SharedKernel.Tests/Pagination/SortSpecTests.cs`:

```csharp
using FluentAssertions;
using Kartova.SharedKernel.Pagination;

namespace Kartova.SharedKernel.Tests.Pagination;

public sealed class SortSpecTests
{
    private sealed record SampleEntity(string Name, DateTimeOffset CreatedAt, Guid Id);

    [Fact]
    public void Construction_captures_field_name_and_key_selector()
    {
        var spec = new SortSpec<SampleEntity>("name", x => x.Name);

        spec.FieldName.Should().Be("name");
        spec.KeySelector.Compile().Invoke(new SampleEntity("x", DateTimeOffset.UtcNow, Guid.NewGuid()))
            .Should().Be("x");
    }

    [Fact]
    public void Records_with_same_field_name_are_equal()
    {
        var a = new SortSpec<SampleEntity>("name", x => x.Name);
        var b = new SortSpec<SampleEntity>("name", x => x.Name);

        // Records compare structurally; expression equality is reference-based, so equality is
        // not guaranteed across two `x => x.Name` literals — but the field name component is.
        a.FieldName.Should().Be(b.FieldName);
    }
}
```

- [ ] **Step 5: Run codec + sort-spec tests (now that exception types exist)**

Run: `cmd //c "dotnet test tests/Kartova.SharedKernel.Tests/Kartova.SharedKernel.Tests.csproj --filter FullyQualifiedName~Pagination --tl:off"`
Expected: PASS (CursorCodecTests + SortSpecTests, ~8 tests).

If FAIL: most likely a JSON-element unwrap edge case in `CursorCodec.Decode`. Check that `JsonElement` is the actual deserialized type for `object?` properties.

- [ ] **Step 6: Commit**

```bash
git add src/Kartova.SharedKernel/Pagination/CursorCodec.cs \
        src/Kartova.SharedKernel/Pagination/InvalidCursorException.cs \
        src/Kartova.SharedKernel/Pagination/InvalidSortFieldException.cs \
        src/Kartova.SharedKernel/Pagination/SortSpec.cs \
        tests/Kartova.SharedKernel.Tests/Pagination/CursorCodecTests.cs \
        tests/Kartova.SharedKernel.Tests/Pagination/SortSpecTests.cs
git commit -m "feat(sharedkernel): CursorCodec, SortSpec, paging exceptions + unit tests (ADR-0095)"
```

---

### Task 5: ToCursorPagedAsync extension (TDD with sqlite)

**Files:**
- Create: `src/Kartova.SharedKernel/Pagination/QueryablePagingExtensions.cs`
- Create: `tests/Kartova.SharedKernel.Tests/Pagination/QueryablePagingExtensionsTests.cs`

- [ ] **Step 1: Write failing tests using sqlite-backed in-memory DbContext**

Create `tests/Kartova.SharedKernel.Tests/Pagination/QueryablePagingExtensionsTests.cs`:

```csharp
using FluentAssertions;
using Kartova.SharedKernel.Pagination;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Kartova.SharedKernel.Tests.Pagination;

public sealed class QueryablePagingExtensionsTests : IAsyncLifetime
{
    private SqliteConnection _conn = null!;
    private TestDbContext _db = null!;

    public sealed class TestRow
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public DateTimeOffset CreatedAt { get; set; }
    }

    public sealed class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> opts) : base(opts) { }
        public DbSet<TestRow> Rows => Set<TestRow>();
    }

    private static readonly SortSpec<TestRow> ByCreatedAt = new("createdAt", x => x.CreatedAt);
    private static readonly SortSpec<TestRow> ByName = new("name", x => x.Name);

    public async Task InitializeAsync()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        await _conn.OpenAsync();
        var opts = new DbContextOptionsBuilder<TestDbContext>().UseSqlite(_conn).Options;
        _db = new TestDbContext(opts);
        await _db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _conn.DisposeAsync();
    }

    private async Task SeedAsync(int count)
    {
        var origin = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        for (var i = 0; i < count; i++)
        {
            _db.Rows.Add(new TestRow
            {
                Id = Guid.Parse($"00000000-0000-0000-0000-{i:D12}"),
                Name = $"row-{i:D3}",
                CreatedAt = origin.AddMinutes(i),
            });
        }
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task EmptyTable_returns_empty_page_with_null_next()
    {
        var page = await _db.Rows.ToCursorPagedAsync(
            ByCreatedAt, SortOrder.Asc, cursor: null, limit: 10, x => x.Id, CancellationToken.None);

        page.Items.Should().BeEmpty();
        page.NextCursor.Should().BeNull();
        page.PrevCursor.Should().BeNull();
    }

    [Fact]
    public async Task SinglePage_returns_all_rows_with_null_next()
    {
        await SeedAsync(5);

        var page = await _db.Rows.ToCursorPagedAsync(
            ByCreatedAt, SortOrder.Asc, cursor: null, limit: 10, x => x.Id, CancellationToken.None);

        page.Items.Should().HaveCount(5);
        page.NextCursor.Should().BeNull();
    }

    [Fact]
    public async Task ExactLimit_does_not_emit_next_cursor()
    {
        await SeedAsync(5);

        var page = await _db.Rows.ToCursorPagedAsync(
            ByCreatedAt, SortOrder.Asc, cursor: null, limit: 5, x => x.Id, CancellationToken.None);

        page.Items.Should().HaveCount(5);
        page.NextCursor.Should().BeNull();
    }

    [Fact]
    public async Task LimitPlusOne_emits_next_cursor_and_trims()
    {
        await SeedAsync(6);

        var page = await _db.Rows.ToCursorPagedAsync(
            ByCreatedAt, SortOrder.Asc, cursor: null, limit: 5, x => x.Id, CancellationToken.None);

        page.Items.Should().HaveCount(5);
        page.NextCursor.Should().NotBeNull();
    }

    [Fact]
    public async Task PagingForward_yields_no_duplicates_no_skips()
    {
        await SeedAsync(20);

        var seen = new List<Guid>();
        string? cursor = null;
        do
        {
            var page = await _db.Rows.ToCursorPagedAsync(
                ByCreatedAt, SortOrder.Asc, cursor, limit: 7, x => x.Id, CancellationToken.None);
            seen.AddRange(page.Items.Select(r => r.Id));
            cursor = page.NextCursor;
        } while (cursor is not null);

        seen.Should().HaveCount(20);
        seen.Distinct().Should().HaveCount(20);
        seen.Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task DescendingOrder_returns_rows_in_reverse()
    {
        await SeedAsync(3);

        var page = await _db.Rows.ToCursorPagedAsync(
            ByCreatedAt, SortOrder.Desc, cursor: null, limit: 10, x => x.Id, CancellationToken.None);

        page.Items.Select(r => r.Name).Should().Equal("row-002", "row-001", "row-000");
    }

    [Fact]
    public async Task TieOnSortValue_uses_id_as_stable_tiebreaker()
    {
        // Three rows with identical CreatedAt, different ids.
        var t = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        _db.Rows.AddRange(
            new TestRow { Id = Guid.Parse("00000000-0000-0000-0000-000000000003"), Name = "c", CreatedAt = t },
            new TestRow { Id = Guid.Parse("00000000-0000-0000-0000-000000000001"), Name = "a", CreatedAt = t },
            new TestRow { Id = Guid.Parse("00000000-0000-0000-0000-000000000002"), Name = "b", CreatedAt = t });
        await _db.SaveChangesAsync();

        // Page through 1 row at a time — tiebreaker MUST give a deterministic order.
        var first = await _db.Rows.ToCursorPagedAsync(
            ByCreatedAt, SortOrder.Asc, cursor: null, limit: 1, x => x.Id, CancellationToken.None);
        var second = await _db.Rows.ToCursorPagedAsync(
            ByCreatedAt, SortOrder.Asc, first.NextCursor, limit: 1, x => x.Id, CancellationToken.None);
        var third = await _db.Rows.ToCursorPagedAsync(
            ByCreatedAt, SortOrder.Asc, second.NextCursor, limit: 1, x => x.Id, CancellationToken.None);

        first.Items.Single().Name.Should().Be("a");
        second.Items.Single().Name.Should().Be("b");
        third.Items.Single().Name.Should().Be("c");
        third.NextCursor.Should().BeNull();
    }

    [Fact]
    public async Task DirectionMismatch_between_cursor_and_request_throws()
    {
        await SeedAsync(5);
        var ascPage = await _db.Rows.ToCursorPagedAsync(
            ByCreatedAt, SortOrder.Asc, cursor: null, limit: 2, x => x.Id, CancellationToken.None);

        var act = async () => await _db.Rows.ToCursorPagedAsync(
            ByCreatedAt, SortOrder.Desc, ascPage.NextCursor, limit: 2, x => x.Id, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidCursorException>();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail (compilation error)**

Run: `cmd //c "dotnet test tests/Kartova.SharedKernel.Tests/Kartova.SharedKernel.Tests.csproj --filter FullyQualifiedName~QueryablePagingExtensionsTests --tl:off"`
Expected: compilation FAIL ("ToCursorPagedAsync does not exist").

If `Microsoft.Data.Sqlite` or `Microsoft.EntityFrameworkCore.Sqlite` are missing from the test project, add them:

```bash
cmd //c "dotnet add tests/Kartova.SharedKernel.Tests/Kartova.SharedKernel.Tests.csproj package Microsoft.EntityFrameworkCore.Sqlite --version 10.0.0"
```

- [ ] **Step 3: Implement `QueryablePagingExtensions.cs`**

```csharp
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace Kartova.SharedKernel.Pagination;

/// <summary>
/// EF Core <see cref="IQueryable{T}"/> extension that applies cursor-based
/// keyset pagination per ADR-0095. Handlers compose filters, joins, and
/// projection on the queryable, then call this extension at the tail.
/// </summary>
public static class QueryablePagingExtensions
{
    public const int MinLimit = 1;
    public const int MaxLimit = 200;
    public const int DefaultLimit = 50;

    public static async Task<CursorPage<T>> ToCursorPagedAsync<T>(
        this IQueryable<T> source,
        SortSpec<T> sort,
        SortOrder order,
        string? cursor,
        int limit,
        Expression<Func<T, Guid>> idSelector,
        CancellationToken ct)
        where T : class
    {
        if (limit < MinLimit || limit > MaxLimit)
        {
            throw new ArgumentOutOfRangeException(nameof(limit),
                $"limit must be between {MinLimit} and {MaxLimit}.");
        }

        IQueryable<T> q = source;

        if (cursor is not null)
        {
            var decoded = CursorCodec.Decode(cursor);
            if (decoded.Direction != order)
            {
                throw new InvalidCursorException(
                    $"Cursor was issued for direction '{decoded.Direction}' but request uses '{order}'.");
            }
            q = ApplyKeysetFilter(q, sort.KeySelector, idSelector, decoded.SortValue, decoded.Id, order);
        }

        q = order == SortOrder.Asc
            ? q.OrderBy(sort.KeySelector).ThenBy(idSelector)
            : q.OrderByDescending(sort.KeySelector).ThenByDescending(idSelector);

        var rows = await q.Take(limit + 1).ToListAsync(ct);

        string? nextCursor = null;
        if (rows.Count > limit)
        {
            rows.RemoveAt(rows.Count - 1);
            var lastKept = rows[^1];
            var sortValue = sort.KeySelector.Compile().Invoke(lastKept)!;
            var id = idSelector.Compile().Invoke(lastKept);
            nextCursor = CursorCodec.Encode(NormalizeForCursor(sortValue), id, order);
        }

        return new CursorPage<T>(rows, nextCursor, PrevCursor: null);
    }

    /// <summary>
    /// Applies <c>WHERE (sortKey, id) > (?, ?)</c> for asc, reversed for desc.
    /// Built as an expression tree so EF translates to a row-constructor comparison
    /// in PostgreSQL (and to a logically equivalent disjunction on sqlite during tests).
    /// </summary>
    private static IQueryable<T> ApplyKeysetFilter<T>(
        IQueryable<T> source,
        Expression<Func<T, object>> keySelector,
        Expression<Func<T, Guid>> idSelector,
        object cursorSortValue,
        Guid cursorId,
        SortOrder order)
    {
        // Build: (sortKey > c.sortValue) || (sortKey == c.sortValue && id > c.id)
        // (reverse the comparators for desc)
        var param = Expression.Parameter(typeof(T), "x");
        var keyBody = ReplaceParameter(keySelector.Body, keySelector.Parameters[0], param);
        var idBody = ReplaceParameter(idSelector.Body, idSelector.Parameters[0], param);

        // Cursor sort value may be JsonElement-unwrapped (e.g. long) but key may be int —
        // convert via the key's underlying type. Both keyBody and the constant get unwrapped to object,
        // then we compare via Expression.Call to Object.Equals / IComparable.CompareTo equivalents.
        // For simplicity and correctness across types, route through a typed helper:
        var keyType = ((UnaryExpression)keyBody).Operand.Type;
        var unwrappedKey = ((UnaryExpression)keyBody).Operand;
        var typedConstant = Expression.Constant(ConvertCursorValue(cursorSortValue, keyType), keyType);

        Expression keyGreater = order == SortOrder.Asc
            ? Expression.GreaterThan(unwrappedKey, typedConstant)
            : Expression.LessThan(unwrappedKey, typedConstant);
        Expression keyEqual = Expression.Equal(unwrappedKey, typedConstant);
        Expression idGreater = order == SortOrder.Asc
            ? Expression.GreaterThan(idBody, Expression.Constant(cursorId))
            : Expression.LessThan(idBody, Expression.Constant(cursorId));

        var disjunction = Expression.OrElse(keyGreater, Expression.AndAlso(keyEqual, idGreater));
        var lambda = Expression.Lambda<Func<T, bool>>(disjunction, param);
        return source.Where(lambda);
    }

    private static Expression ReplaceParameter(Expression body, ParameterExpression from, ParameterExpression to)
        => new ParameterReplaceVisitor(from, to).Visit(body);

    private sealed class ParameterReplaceVisitor : ExpressionVisitor
    {
        private readonly ParameterExpression _from;
        private readonly ParameterExpression _to;
        public ParameterReplaceVisitor(ParameterExpression from, ParameterExpression to) { _from = from; _to = to; }
        protected override Expression VisitParameter(ParameterExpression node) =>
            node == _from ? _to : base.VisitParameter(node);
    }

    private static object ConvertCursorValue(object value, Type targetType)
    {
        if (targetType == typeof(DateTimeOffset) && value is string s)
        {
            return DateTimeOffset.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
        }
        if (targetType == typeof(DateTime) && value is string s2)
        {
            return DateTime.Parse(s2, System.Globalization.CultureInfo.InvariantCulture).ToUniversalTime();
        }
        return Convert.ChangeType(value, targetType, System.Globalization.CultureInfo.InvariantCulture)!;
    }

    private static object NormalizeForCursor(object value) => value switch
    {
        DateTimeOffset dto => dto.UtcDateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
        DateTime dt => dt.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture),
        _ => value,
    };
}
```

- [ ] **Step 4: Run tests — expect green**

Run: `cmd //c "dotnet test tests/Kartova.SharedKernel.Tests/Kartova.SharedKernel.Tests.csproj --filter FullyQualifiedName~QueryablePagingExtensionsTests --tl:off"`
Expected: 8 tests PASS.

If `ApplyKeysetFilter`'s expression-tree composition fails ("Expression of type 'X' cannot be used for parameter of type 'Y'"), the most likely cause is the `keyBody` not being a `UnaryExpression` (occurs when the selector returns a reference type and no boxing is needed). Fallback: detect whether `keyBody` is a `UnaryExpression { NodeType: Convert }` and unwrap only if so; otherwise use `keyBody` directly.

- [ ] **Step 5: Commit**

```bash
git add src/Kartova.SharedKernel/Pagination/QueryablePagingExtensions.cs \
        tests/Kartova.SharedKernel.Tests/Pagination/QueryablePagingExtensionsTests.cs \
        tests/Kartova.SharedKernel.Tests/Kartova.SharedKernel.Tests.csproj
git commit -m "feat(sharedkernel): ToCursorPagedAsync keyset extension + sqlite unit tests (ADR-0095)"
```

---

### Task 6: PagingExceptionHandler (RFC 7807 mapping)

**Files:**
- Create: `src/Kartova.SharedKernel.AspNetCore/PagingExceptionHandler.cs`
- Create: `tests/Kartova.SharedKernel.AspNetCore.Tests/PagingExceptionHandlerTests.cs`
- Modify: `src/Kartova.Api/Program.cs` (register the handler)

- [ ] **Step 1: Find the existing exception handler registration**

Run: `cmd //c "grep -n IExceptionHandler src/Kartova.Api/Program.cs src/Kartova.SharedKernel.AspNetCore/*.cs"`

Read `DomainValidationExceptionHandler.cs` (or equivalent) to mirror its style — namespace, `ProblemTypes` constants, `IExceptionHandler` implementation pattern.

- [ ] **Step 2: Add a paging-specific entry to the existing `ProblemTypes` static class**

Open `src/Kartova.SharedKernel.AspNetCore/ProblemTypes.cs` (location verified by step 1). Append:

```csharp
public const string InvalidSortField = "https://kartova.dev/problems/invalid-sort-field";
public const string InvalidSortOrder = "https://kartova.dev/problems/invalid-sort-order";
public const string InvalidCursor = "https://kartova.dev/problems/invalid-cursor";
public const string InvalidLimit = "https://kartova.dev/problems/invalid-limit";
```

- [ ] **Step 3: Write tests for the handler**

Create `tests/Kartova.SharedKernel.AspNetCore.Tests/PagingExceptionHandlerTests.cs`:

```csharp
using FluentAssertions;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Pagination;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Kartova.SharedKernel.AspNetCore.Tests;

public sealed class PagingExceptionHandlerTests
{
    [Fact]
    public async Task InvalidSortFieldException_maps_to_400_with_allowed_fields()
    {
        var handler = new PagingExceptionHandler();
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();
        var ex = new InvalidSortFieldException("foo", new[] { "createdAt", "name" });

        var handled = await handler.TryHandleAsync(ctx, ex, CancellationToken.None);

        handled.Should().BeTrue();
        ctx.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        ctx.Response.ContentType.Should().StartWith("application/problem+json");

        ctx.Response.Body.Position = 0;
        var body = await new StreamReader(ctx.Response.Body).ReadToEndAsync();
        body.Should().Contain(ProblemTypes.InvalidSortField);
        body.Should().Contain("createdAt");
        body.Should().Contain("name");
    }

    [Fact]
    public async Task InvalidCursorException_maps_to_400()
    {
        var handler = new PagingExceptionHandler();
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();
        var ex = new InvalidCursorException("Cursor JSON is malformed.");

        var handled = await handler.TryHandleAsync(ctx, ex, CancellationToken.None);

        handled.Should().BeTrue();
        ctx.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);

        ctx.Response.Body.Position = 0;
        var body = await new StreamReader(ctx.Response.Body).ReadToEndAsync();
        body.Should().Contain(ProblemTypes.InvalidCursor);
    }

    [Fact]
    public async Task UnrelatedException_returns_false()
    {
        var handler = new PagingExceptionHandler();
        var ctx = new DefaultHttpContext();

        var handled = await handler.TryHandleAsync(ctx, new InvalidOperationException("x"), CancellationToken.None);

        handled.Should().BeFalse();
    }
}
```

- [ ] **Step 4: Run tests to verify they fail**

Run: `cmd //c "dotnet test tests/Kartova.SharedKernel.AspNetCore.Tests/Kartova.SharedKernel.AspNetCore.Tests.csproj --filter FullyQualifiedName~PagingExceptionHandlerTests --tl:off"`
Expected: compilation FAIL.

- [ ] **Step 5: Implement `PagingExceptionHandler`**

```csharp
using Kartova.SharedKernel.Pagination;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Kartova.SharedKernel.AspNetCore;

/// <summary>
/// Maps pagination/sort exceptions to RFC 7807 400 responses per ADR-0091 + ADR-0095.
/// Registered in <c>Program.cs</c> alongside <c>DomainValidationExceptionHandler</c>.
/// </summary>
public sealed class PagingExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        switch (exception)
        {
            case InvalidSortFieldException sortEx:
                await WriteProblemAsync(
                    httpContext,
                    type: ProblemTypes.InvalidSortField,
                    title: "Invalid sort field",
                    detail: sortEx.Message,
                    extensions: new Dictionary<string, object?>
                    {
                        ["fieldName"] = sortEx.FieldName,
                        ["allowedFields"] = sortEx.AllowedFields,
                    },
                    cancellationToken);
                return true;

            case InvalidCursorException cursorEx:
                await WriteProblemAsync(
                    httpContext,
                    type: ProblemTypes.InvalidCursor,
                    title: "Invalid cursor",
                    detail: cursorEx.Message,
                    extensions: null,
                    cancellationToken);
                return true;

            default:
                return false;
        }
    }

    private static async Task WriteProblemAsync(
        HttpContext ctx,
        string type,
        string title,
        string detail,
        IDictionary<string, object?>? extensions,
        CancellationToken ct)
    {
        var problem = new ProblemDetails
        {
            Type = type,
            Title = title,
            Status = StatusCodes.Status400BadRequest,
            Detail = detail,
            Instance = ctx.Request.Path,
        };
        if (extensions is not null)
        {
            foreach (var (k, v) in extensions) problem.Extensions[k] = v;
        }

        ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
        ctx.Response.ContentType = "application/problem+json";
        await ctx.Response.WriteAsJsonAsync(problem, ct);
    }
}
```

- [ ] **Step 6: Register handler in `Program.cs`**

In `src/Kartova.Api/Program.cs`, find the line that registers `DomainValidationExceptionHandler` (e.g., `builder.Services.AddExceptionHandler<DomainValidationExceptionHandler>();`) and add immediately above or below:

```csharp
builder.Services.AddExceptionHandler<PagingExceptionHandler>();
```

Add `using Kartova.SharedKernel.AspNetCore;` if not already present. Order matters: ASP.NET Core invokes handlers in registration order; since `PagingExceptionHandler` and `DomainValidationExceptionHandler` handle disjoint exception types, ordering between them is irrelevant — but both must come before any catch-all handler.

- [ ] **Step 7: Run tests + full build**

Run: `cmd //c "dotnet test tests/Kartova.SharedKernel.AspNetCore.Tests/Kartova.SharedKernel.AspNetCore.Tests.csproj --filter FullyQualifiedName~PagingExceptionHandlerTests --tl:off"`
Expected: 3 tests PASS.

Run: `cmd //c "dotnet build Kartova.slnx --tl:off"`
Expected: 0 warnings, 0 errors.

- [ ] **Step 8: Commit**

```bash
git add src/Kartova.SharedKernel.AspNetCore/PagingExceptionHandler.cs \
        src/Kartova.SharedKernel.AspNetCore/ProblemTypes.cs \
        src/Kartova.Api/Program.cs \
        tests/Kartova.SharedKernel.AspNetCore.Tests/PagingExceptionHandlerTests.cs
git commit -m "feat(sharedkernel.aspnetcore): PagingExceptionHandler — paging exceptions → RFC 7807 400 (ADR-0095)"
```

---

## Phase C — Catalog reference implementation

### Task 7: ApplicationSortField + ApplicationSortSpecs

**Files:**
- Create: `src/Modules/Catalog/Kartova.Catalog.Contracts/ApplicationSortField.cs`
- Create: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/ApplicationSortSpecs.cs`

- [ ] **Step 1: Create `ApplicationSortField` enum (in Contracts)**

```csharp
using System.Diagnostics.CodeAnalysis;

namespace Kartova.Catalog.Contracts;

/// <summary>
/// Public sort-field allowlist for <c>GET /api/v1/catalog/applications</c>.
/// Surfaces in OpenAPI as <c>SortByApplications</c>. ADR-0095.
/// </summary>
[ExcludeFromCodeCoverage]
public enum ApplicationSortField
{
    CreatedAt,
    Name
}
```

- [ ] **Step 2: Create `ApplicationSortSpecs` (in Infrastructure)**

```csharp
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.Pagination;

namespace Kartova.Catalog.Infrastructure;

/// <summary>
/// Per-resource sort allowlist for the Applications list endpoint, co-located
/// with the handler that enforces it (ADR-0095 §5).
/// </summary>
internal static class ApplicationSortSpecs
{
    public static readonly SortSpec<Application> CreatedAt =
        new("createdAt", x => x.CreatedAt);

    public static readonly SortSpec<Application> Name =
        new("name", x => x.Name);

    public static readonly IReadOnlyList<string> AllowedFieldNames = [CreatedAt.FieldName, Name.FieldName];

    public static SortSpec<Application> Resolve(ApplicationSortField field) => field switch
    {
        Contracts.ApplicationSortField.CreatedAt => CreatedAt,
        Contracts.ApplicationSortField.Name => Name,
        _ => throw new InvalidSortFieldException(field.ToString(), AllowedFieldNames),
    };
}
```

(Adjust `using Kartova.Catalog.Domain;` if `Application` lives in a different namespace — verify with: `cmd //c "grep -rn 'class Application' src/Modules/Catalog/Kartova.Catalog.Domain/"`).

- [ ] **Step 3: Build green check**

Run: `cmd //c "dotnet build src/Modules/Catalog/Kartova.Catalog.Infrastructure/Kartova.Catalog.Infrastructure.csproj --tl:off"`
Expected: 0 warnings, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add src/Modules/Catalog/Kartova.Catalog.Contracts/ApplicationSortField.cs \
        src/Modules/Catalog/Kartova.Catalog.Infrastructure/ApplicationSortSpecs.cs
git commit -m "feat(catalog): ApplicationSortField enum + ApplicationSortSpecs allowlist (ADR-0095)"
```

---

### Task 8: ListApplicationsQuery + ListApplicationsHandler refactor (TDD)

**Files:**
- Modify: `src/Modules/Catalog/Kartova.Catalog.Application/ListApplicationsQuery.cs`
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/ListApplicationsHandler.cs`

This task is "modify-with-tests" rather than pure TDD — the handler exists and unit tests live in the integration test project (Task 10). Here we change the type signatures, then verify the build is green; functional verification happens in Task 10.

- [ ] **Step 1: Modify `ListApplicationsQuery.cs`**

Replace the contents:

```csharp
using Kartova.Catalog.Contracts;
using Kartova.SharedKernel.Pagination;

namespace Kartova.Catalog.Application;

/// <summary>
/// List applications visible to the current tenant (RLS-filtered). ADR-0095.
/// </summary>
public sealed record ListApplicationsQuery(
    ApplicationSortField SortBy,
    SortOrder SortOrder,
    string? Cursor,
    int Limit);
```

- [ ] **Step 2: Rewrite `ListApplicationsHandler.cs`**

Replace contents:

```csharp
using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Kartova.SharedKernel.Pagination;

namespace Kartova.Catalog.Infrastructure;

/// <summary>
/// Handler for <see cref="ListApplicationsQuery"/>. RLS auto-filters cross-tenant
/// rows so the result set is implicitly scoped to the current tenant (ADR-0090).
/// Pagination applied via <see cref="QueryablePagingExtensions.ToCursorPagedAsync{T}"/>
/// (ADR-0095).
/// </summary>
public sealed class ListApplicationsHandler
{
    public async Task<CursorPage<ApplicationResponse>> Handle(
        ListApplicationsQuery q,
        CatalogDbContext db,
        CancellationToken ct)
    {
        var spec = ApplicationSortSpecs.Resolve(q.SortBy);

        var page = await db.Applications
            .ToCursorPagedAsync(spec, q.SortOrder, q.Cursor, q.Limit, x => x.Id, ct);

        var items = page.Items.Select(r => r.ToResponse()).ToList();
        return new CursorPage<ApplicationResponse>(items, page.NextCursor, PrevCursor: null);
    }
}
```

- [ ] **Step 3: Build green check (handler now requires updated endpoint delegate — Task 9)**

Run: `cmd //c "dotnet build src/Modules/Catalog/Kartova.Catalog.Infrastructure/Kartova.Catalog.Infrastructure.csproj --tl:off"`
Expected: build succeeds for the Infrastructure project. Building the API project will fail until Task 9 — that is expected.

- [ ] **Step 4: No commit yet — combine with Task 9**

Hold the diff; Task 9 finishes the wire-shape change at the endpoint boundary. They commit together.

---

### Task 9: CatalogEndpointDelegates — wire query params + envelope

**Files:**
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogEndpointDelegates.cs`

- [ ] **Step 1: Replace `ListApplicationsAsync`**

In `CatalogEndpointDelegates.cs`, replace the existing `ListApplicationsAsync`:

```csharp
/// <summary>
/// GET list of Applications visible in current tenant. Direct synchronous
/// handler dispatch to preserve the HTTP request scope's <c>ITenantScope</c>
/// (see comment on <see cref="RegisterApplicationAsync"/>). RLS auto-filters
/// cross-tenant rows (ADR-0090). Cursor-paginated per ADR-0095.
/// </summary>
internal static async Task<IResult> ListApplicationsAsync(
    [FromQuery] ApplicationSortField? sortBy,
    [FromQuery] SortOrder? sortOrder,
    [FromQuery] string? cursor,
    [FromQuery] int? limit,
    ListApplicationsHandler handler,
    CatalogDbContext db,
    CancellationToken ct)
{
    var query = new ListApplicationsQuery(
        SortBy: sortBy ?? ApplicationSortField.CreatedAt,
        SortOrder: sortOrder ?? SortOrder.Desc,
        Cursor: cursor,
        Limit: limit ?? QueryablePagingExtensions.DefaultLimit);

    var page = await handler.Handle(query, db, ct);
    return Results.Ok(page);
}
```

Add the `using Kartova.SharedKernel.Pagination;` import at the top of the file if missing.

- [ ] **Step 2: Build the API project**

Run: `cmd //c "dotnet build src/Kartova.Api/Kartova.Api.csproj --tl:off"`
Expected: 0 warnings, 0 errors.

If `[FromQuery]` enum binding fails at runtime (model binder cannot parse "createdAt"), the issue is case sensitivity — ASP.NET Core's enum model binder is case-insensitive by default, so this should work. If it does not, register `JsonStringEnumConverter` globally (it should already be registered for the existing API; verify in `Program.cs`).

- [ ] **Step 3: Full solution build green check**

Run: `cmd //c "dotnet build Kartova.slnx --tl:off"`
Expected: 0 warnings, 0 errors. Tests not yet updated may fail at unit-test runtime — that is expected and resolved in Task 10.

- [ ] **Step 4: Commit (Task 8 + Task 9 combined)**

```bash
git add src/Modules/Catalog/Kartova.Catalog.Application/ListApplicationsQuery.cs \
        src/Modules/Catalog/Kartova.Catalog.Infrastructure/ListApplicationsHandler.cs \
        src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogEndpointDelegates.cs
git commit -m "feat(catalog): ListApplications — cursor pagination wire shape (ADR-0095)"
```

---

### Task 10: Catalog integration tests (Testcontainers, real PostgreSQL)

**Files:**
- Create: `tests/Kartova.Catalog.IntegrationTests/Kartova.Catalog.IntegrationTests.csproj`
- Create: `tests/Kartova.Catalog.IntegrationTests/ListApplicationsPaginationTests.cs`
- Modify: existing `tests/Kartova.Api.IntegrationTests/` if shared fixtures live there — copy the WebApplicationFactory pattern.

If a `Kartova.Catalog.IntegrationTests` project already exists, skip the project-creation step and add to it. Verify with: `cmd //c "ls tests/"`.

- [ ] **Step 1: Inspect existing integration test project**

Read `tests/Kartova.Api.IntegrationTests/Kartova.Api.IntegrationTests.csproj` and the test class for `RegisterApplication` (any test that already exercises a Catalog endpoint via Testcontainers). The new pagination tests should mirror this fixture pattern — same `WebApplicationFactory`, same Testcontainers PostgreSQL image, same JWT bearer setup against `Kartova.Testing.Auth`.

If tests for Catalog endpoints already live in `Kartova.Api.IntegrationTests`, **add the new tests there** rather than spawning a new project. Adjust subsequent paths in this task accordingly. The plan continues assuming a co-located file `tests/Kartova.Api.IntegrationTests/Catalog/ListApplicationsPaginationTests.cs`.

- [ ] **Step 2: Write integration tests**

Create `tests/Kartova.Api.IntegrationTests/Catalog/ListApplicationsPaginationTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Kartova.Catalog.Contracts;
using Kartova.SharedKernel.Pagination;
using Kartova.Testing.Auth;

namespace Kartova.Api.IntegrationTests.Catalog;

/// <summary>
/// Pagination + sort + RLS integration tests for GET /api/v1/catalog/applications (ADR-0095).
/// Uses the existing WebApplicationFactory + Testcontainers PostgreSQL fixture.
/// </summary>
public sealed class ListApplicationsPaginationTests : IClassFixture<KartovaApiFactory>
{
    private readonly KartovaApiFactory _factory;
    public ListApplicationsPaginationTests(KartovaApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Pages_through_75_apps_in_batches_of_50_yields_no_duplicates_no_skips()
    {
        // Arrange: seed 75 apps for Org A + 75 for Org B (RLS must hide Org B).
        await _factory.SeedApplicationsAsync(SeededOrgs.OrgATenantId, count: 75, namePrefix: "a-");
        await _factory.SeedApplicationsAsync(SeededOrgs.OrgBTenantId, count: 75, namePrefix: "b-");

        var client = _factory.CreateClientForOrgA();

        var allIds = new HashSet<Guid>();
        string? cursor = null;
        var pageCount = 0;
        do
        {
            var url = "/api/v1/catalog/applications?sortBy=createdAt&sortOrder=asc&limit=50"
                + (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}");
            var resp = await client.GetAsync(url);
            resp.StatusCode.Should().Be(HttpStatusCode.OK);
            var page = await resp.Content.ReadFromJsonAsync<CursorPage<ApplicationResponse>>();
            page.Should().NotBeNull();
            foreach (var item in page!.Items)
            {
                allIds.Add(item.Id).Should().BeTrue("each id must appear exactly once");
            }
            cursor = page.NextCursor;
            pageCount++;
        } while (cursor is not null && pageCount < 10);

        allIds.Should().HaveCount(75, "only Org A's 75 rows are visible — RLS hides Org B");
    }

    [Fact]
    public async Task InvalidSortBy_returns_400_with_allowed_fields()
    {
        var client = _factory.CreateClientForOrgA();

        var resp = await client.GetAsync("/api/v1/catalog/applications?sortBy=garbage");

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await resp.Content.ReadAsStringAsync();
        problem.Should().Contain("invalid-sort-field");
        problem.Should().Contain("createdAt");
        problem.Should().Contain("name");
    }

    [Fact]
    public async Task TamperedCursor_returns_400_invalid_cursor()
    {
        var client = _factory.CreateClientForOrgA();

        var resp = await client.GetAsync("/api/v1/catalog/applications?cursor=not-a-valid-cursor!!!");

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("invalid-cursor");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(201)]
    [InlineData(-1)]
    public async Task LimitOutOfRange_returns_400(int limit)
    {
        var client = _factory.CreateClientForOrgA();

        var resp = await client.GetAsync($"/api/v1/catalog/applications?limit={limit}");

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task DefaultParams_match_explicit_createdAt_desc_50()
    {
        await _factory.SeedApplicationsAsync(SeededOrgs.OrgATenantId, count: 5, namePrefix: "x-");
        var client = _factory.CreateClientForOrgA();

        var defaultResp = await client.GetFromJsonAsync<CursorPage<ApplicationResponse>>("/api/v1/catalog/applications");
        var explicitResp = await client.GetFromJsonAsync<CursorPage<ApplicationResponse>>(
            "/api/v1/catalog/applications?sortBy=createdAt&sortOrder=desc&limit=50");

        defaultResp.Should().NotBeNull();
        explicitResp.Should().NotBeNull();
        defaultResp!.Items.Select(i => i.Id).Should().Equal(explicitResp!.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task Deletion_of_cursor_row_mid_pagination_does_not_skip_or_dup()
    {
        // Risk #2 from spec §14: keyset filter `(sortKey, id) > (cursorSortValue, cursorId)`
        // remains correct when the boundary row has been deleted between fetches.
        await _factory.SeedApplicationsAsync(SeededOrgs.OrgATenantId, count: 10, namePrefix: "del-");
        var client = _factory.CreateClientForOrgA();

        var first = await client.GetFromJsonAsync<CursorPage<ApplicationResponse>>(
            "/api/v1/catalog/applications?sortBy=createdAt&sortOrder=asc&limit=4");
        first!.Items.Should().HaveCount(4);

        // Delete one of the boundary rows (the last item of the first page).
        await _factory.DeleteApplicationAsync(SeededOrgs.OrgATenantId, first.Items[^1].Id);

        var second = await client.GetFromJsonAsync<CursorPage<ApplicationResponse>>(
            $"/api/v1/catalog/applications?sortBy=createdAt&sortOrder=asc&limit=4&cursor={Uri.EscapeDataString(first.NextCursor!)}");

        second!.Items.Select(i => i.Id).Should().NotContain(first.Items.Select(i => i.Id),
            "no row from the first page should reappear");
    }
}
```

- [ ] **Step 3: Add helper methods to the test factory**

Locate the existing test factory (likely `tests/Kartova.Api.IntegrationTests/KartovaApiFactory.cs` or similar — verified by step 1). Add:

```csharp
public async Task SeedApplicationsAsync(Guid tenantId, int count, string namePrefix)
{
    using var scope = Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

    // Toggle FORCE RLS off (migrator role) to bulk-seed across tenants for the test.
    await db.Database.ExecuteSqlRawAsync("ALTER TABLE applications NO FORCE ROW LEVEL SECURITY;");
    try
    {
        var origin = DateTimeOffset.UtcNow.AddMinutes(-count);
        for (var i = 0; i < count; i++)
        {
            db.Applications.Add(Application.Create(
                tenantId: tenantId,
                name: $"{namePrefix}{i:D3}",
                displayName: $"{namePrefix.ToUpperInvariant()}{i:D3}",
                description: "",
                ownerUserId: Guid.NewGuid(),
                createdAt: origin.AddMinutes(i)));
        }
        await db.SaveChangesAsync();
    }
    finally
    {
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE applications FORCE ROW LEVEL SECURITY;");
    }
}

public async Task DeleteApplicationAsync(Guid tenantId, Guid applicationId)
{
    using var scope = Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
    await db.Database.ExecuteSqlRawAsync("ALTER TABLE applications NO FORCE ROW LEVEL SECURITY;");
    try
    {
        await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM applications WHERE id = {0} AND tenant_id = {1};",
            applicationId, tenantId);
    }
    finally
    {
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE applications FORCE ROW LEVEL SECURITY;");
    }
}
```

If `Application.Create` does not accept a `createdAt` parameter — i.e., the factory always uses `DateTimeOffset.UtcNow` — three options:
1. Add an internal overload `Application.Create(..., DateTimeOffset createdAt)` for testing.
2. Insert via raw SQL instead.
3. Sleep 1 ms between inserts (slow, brittle).

Prefer option 1: a new internal-visible factory overload guarded by `InternalsVisibleTo(Kartova.Api.IntegrationTests)`.

- [ ] **Step 4: Add `OrgBTenantId` to `SeededOrgs` if missing**

Open `tests/Kartova.Testing.Auth/SeededOrgs.cs`. If only `OrgATenantId` exists, add a second org:

```csharp
public static readonly Guid OrgBTenantId = Guid.Parse("22222222-2222-2222-2222-222222222222");
```

(Mirroring the realm seed pattern — Org B may also need a row in `realm-kartova.json` if the test JWT factory pulls users from there. Check what `CreateClientForOrgA()` does; if it constructs a JWT directly from `OrgATenantId` claim without realm wiring, then no realm change is needed.)

- [ ] **Step 5: Run tests**

Run: `cmd //c "dotnet test tests/Kartova.Api.IntegrationTests/Kartova.Api.IntegrationTests.csproj --filter FullyQualifiedName~ListApplicationsPaginationTests --tl:off"`
Expected: all 7 tests PASS (1 paging-correctness, 1 invalid-sort, 1 invalid-cursor, 3 invalid-limit theory cases, 1 default-params, 1 deletion-mid-pagination).

If TestContainers cannot start PostgreSQL on this machine (Docker not running): note it explicitly in the slice DoD and run after the user confirms Docker is up — never silently skip.

- [ ] **Step 6: Commit**

```bash
git add tests/Kartova.Api.IntegrationTests/Catalog/ListApplicationsPaginationTests.cs \
        tests/Kartova.Api.IntegrationTests/KartovaApiFactory.cs \
        tests/Kartova.Testing.Auth/SeededOrgs.cs
git commit -m "test(catalog): integration tests for cursor pagination + sort + RLS (ADR-0095)"
```

---

## Phase D — Architecture fitness rule

### Task 11: PaginationConventionRules

**Files:**
- Create: `tests/Kartova.ArchitectureTests/PaginationConventionRules.cs`

- [ ] **Step 1: Inspect existing arch test patterns**

Read `tests/Kartova.ArchitectureTests/IModuleRules.cs` and `tests/Kartova.ArchitectureTests/ContractsCoverageRules.cs` to mirror the assembly-discovery pattern (likely uses `AssemblyRegistry.cs` to enumerate module assemblies).

- [ ] **Step 2: Write the rule**

Create `tests/Kartova.ArchitectureTests/PaginationConventionRules.cs`:

```csharp
using FluentAssertions;
using Kartova.SharedKernel.Pagination;
using NetArchTest.Rules;

namespace Kartova.ArchitectureTests;

/// <summary>
/// Enforces ADR-0095 §8: every <c>List*Handler</c> in a module's
/// <c>*.Infrastructure</c> assembly must return <c>Task&lt;CursorPage&lt;T&gt;&gt;</c>,
/// unless the handler class is decorated with <c>[BoundedListResult]</c>.
/// </summary>
public sealed class PaginationConventionRules
{
    [Fact]
    public void List_handlers_in_infrastructure_assemblies_return_CursorPage_or_are_BoundedListResult()
    {
        var infraAssemblies = AssemblyRegistry.AllInfrastructureAssemblies();

        foreach (var asm in infraAssemblies)
        {
            var listHandlers = Types.InAssembly(asm)
                .That()
                .HaveNameMatching(@"^List.*Handler$")
                .And().AreClasses()
                .GetTypes()
                .ToList();

            foreach (var t in listHandlers)
            {
                var bounded = t.GetCustomAttributes(typeof(BoundedListResultAttribute), inherit: false)
                    .Cast<BoundedListResultAttribute>()
                    .FirstOrDefault();

                if (bounded is not null)
                {
                    bounded.Reason.Should().NotBeNullOrWhiteSpace(
                        because: $"{t.FullName} is [BoundedListResult] — reason must be set");
                    continue;
                }

                var handle = t.GetMethod("Handle")
                    ?? throw new InvalidOperationException($"{t.FullName} has no Handle method");
                var ret = handle.ReturnType;

                ret.IsGenericType.Should().BeTrue(
                    because: $"{t.FullName}.Handle must return Task<CursorPage<...>> per ADR-0095");
                ret.GetGenericTypeDefinition().Should().Be(typeof(Task<>));
                var inner = ret.GetGenericArguments()[0];
                inner.IsGenericType.Should().BeTrue(
                    because: $"{t.FullName}.Handle must return Task<CursorPage<...>> per ADR-0095");
                inner.GetGenericTypeDefinition().Should().Be(typeof(CursorPage<>),
                    because: $"{t.FullName}.Handle returns {ret} — must be Task<CursorPage<...>> per ADR-0095, or annotate the class with [BoundedListResult]");
            }
        }
    }
}
```

- [ ] **Step 3: Add `AllInfrastructureAssemblies` helper if absent**

Read `tests/Kartova.ArchitectureTests/AssemblyRegistry.cs`. If the registry does not expose a method to enumerate `*.Infrastructure` assemblies, add it. The pattern likely already exists (used by other arch rules).

If absent, append to `AssemblyRegistry`:

```csharp
public static IReadOnlyList<Assembly> AllInfrastructureAssemblies() =>
    AppDomain.CurrentDomain.GetAssemblies()
        .Where(a => a.GetName().Name?.EndsWith(".Infrastructure", StringComparison.Ordinal) == true)
        .ToList();
```

If using a static module list instead, add `Kartova.Catalog.Infrastructure` to that list.

- [ ] **Step 4: Run the rule**

Run: `cmd //c "dotnet test tests/Kartova.ArchitectureTests/Kartova.ArchitectureTests.csproj --filter FullyQualifiedName~PaginationConventionRules --tl:off"`
Expected: PASS — `ListApplicationsHandler` returns `Task<CursorPage<ApplicationResponse>>` after Tasks 8–9.

If FAIL: confirm `Kartova.Catalog.Infrastructure` is loaded into `AppDomain.CurrentDomain` — arch test projects often need an explicit `ProjectReference` to ensure runtime load. Add it to `Kartova.ArchitectureTests.csproj` if missing.

- [ ] **Step 5: Commit**

```bash
git add tests/Kartova.ArchitectureTests/PaginationConventionRules.cs \
        tests/Kartova.ArchitectureTests/AssemblyRegistry.cs \
        tests/Kartova.ArchitectureTests/Kartova.ArchitectureTests.csproj
git commit -m "test(arch): PaginationConventionRules — List*Handler must return CursorPage<T> (ADR-0095 §8)"
```

---

## Phase E — Frontend primitives

### Task 12: Frontend types + useCursorList hook (Vitest)

**Files:**
- Create: `web/src/lib/list/types.ts`
- Create: `web/src/lib/list/useCursorList.ts`
- Create: `web/src/lib/list/__tests__/use-cursor-list.test.tsx`

- [ ] **Step 1: Create `types.ts`**

```ts
export type SortDirection = "asc" | "desc";

export interface CursorListResult<TItem> {
  items: TItem[];
  isLoading: boolean;
  isFetching: boolean;
  isError: boolean;
  hasNext: boolean;
  hasPrev: boolean;
  goNext: () => void;
  goPrev: () => void;
  reset: () => void;
}

export interface CursorPageEnvelope<TItem> {
  items: TItem[];
  nextCursor: string | null;
  prevCursor: string | null;
}
```

- [ ] **Step 2: Write failing tests for `useCursorList`**

Create `web/src/lib/list/__tests__/use-cursor-list.test.tsx`:

```tsx
import { describe, expect, it, vi } from "vitest";
import { renderHook, act, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { useCursorList } from "../useCursorList";
import type { CursorPageEnvelope } from "../types";

function wrapper(qc: QueryClient) {
  return ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={qc}>{children}</QueryClientProvider>
  );
}

interface Row { id: string; name: string; }

function fetchPageMock(pages: CursorPageEnvelope<Row>[]) {
  let calls = 0;
  return vi.fn(async (cursor: string | undefined) => {
    const idx = cursor ? Number(cursor) : 0;
    calls++;
    return pages[idx] ?? { items: [], nextCursor: null, prevCursor: null };
  });
}

describe("useCursorList", () => {
  it("loads first page on mount", async () => {
    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const fetchPage = fetchPageMock([
      { items: [{ id: "a", name: "A" }, { id: "b", name: "B" }], nextCursor: "1", prevCursor: null },
      { items: [{ id: "c", name: "C" }], nextCursor: null, prevCursor: null },
    ]);

    const { result } = renderHook(
      () => useCursorList<Row>({ queryKey: ["t"], fetchPage }),
      { wrapper: wrapper(qc) }
    );

    await waitFor(() => expect(result.current.isLoading).toBe(false));
    expect(result.current.items.map(i => i.id)).toEqual(["a", "b"]);
    expect(result.current.hasNext).toBe(true);
    expect(result.current.hasPrev).toBe(false);
  });

  it("goNext advances and goPrev rewinds the cursor stack", async () => {
    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const fetchPage = fetchPageMock([
      { items: [{ id: "a", name: "A" }], nextCursor: "1", prevCursor: null },
      { items: [{ id: "b", name: "B" }], nextCursor: null, prevCursor: null },
    ]);

    const { result } = renderHook(
      () => useCursorList<Row>({ queryKey: ["t"], fetchPage }),
      { wrapper: wrapper(qc) }
    );
    await waitFor(() => expect(result.current.isLoading).toBe(false));

    act(() => { result.current.goNext(); });
    await waitFor(() => expect(result.current.items.map(i => i.id)).toEqual(["b"]));
    expect(result.current.hasPrev).toBe(true);
    expect(result.current.hasNext).toBe(false);

    act(() => { result.current.goPrev(); });
    await waitFor(() => expect(result.current.items.map(i => i.id)).toEqual(["a"]));
    expect(result.current.hasPrev).toBe(false);
  });

  it("reset clears the cursor stack and refetches first page", async () => {
    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const fetchPage = fetchPageMock([
      { items: [{ id: "a", name: "A" }], nextCursor: "1", prevCursor: null },
      { items: [{ id: "b", name: "B" }], nextCursor: null, prevCursor: null },
    ]);

    const { result } = renderHook(
      () => useCursorList<Row>({ queryKey: ["t"], fetchPage }),
      { wrapper: wrapper(qc) }
    );
    await waitFor(() => expect(result.current.isLoading).toBe(false));

    act(() => { result.current.goNext(); });
    await waitFor(() => expect(result.current.items.map(i => i.id)).toEqual(["b"]));

    act(() => { result.current.reset(); });
    await waitFor(() => expect(result.current.items.map(i => i.id)).toEqual(["a"]));
    expect(result.current.hasPrev).toBe(false);
  });
});
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `cmd //c "cd web && npm run test -- --run lib/list"`
Expected: FAIL — module `../useCursorList` not found.

- [ ] **Step 4: Implement `useCursorList`**

Create `web/src/lib/list/useCursorList.ts`:

```ts
import { useCallback, useState } from "react";
import { useQuery, type QueryKey } from "@tanstack/react-query";
import type { CursorListResult, CursorPageEnvelope } from "./types";

interface UseCursorListOptions<TItem> {
  queryKey: QueryKey;
  fetchPage: (cursor: string | undefined) => Promise<CursorPageEnvelope<TItem>>;
  /** Garbage-collection time for cached pages (ms). Default 5 min. */
  gcTime?: number;
}

/**
 * Generic Prev/Next cursor-paginated list driver. Wraps TanStack Query's
 * useQuery (one query per page) and maintains an in-memory cursor stack so
 * "Prev" works without the server emitting prevCursor (ADR-0095 §5.2).
 *
 * - The cursor stack is `[undefined, c1, c2, ...]`; the last entry is the
 *   current page's cursor. goNext pushes the next-cursor; goPrev pops.
 * - Sort changes that mutate `queryKey` automatically reset the stack via
 *   useEffect dependency on the serialized key.
 */
export function useCursorList<TItem>(
  options: UseCursorListOptions<TItem>,
): CursorListResult<TItem> {
  const { queryKey, fetchPage, gcTime = 5 * 60 * 1000 } = options;
  const [stack, setStack] = useState<(string | undefined)[]>([undefined]);
  const currentCursor = stack[stack.length - 1];

  // Reset stack when queryKey identity changes (sort flip).
  // Serialize for stable dependency.
  const keyStr = JSON.stringify(queryKey);
  // eslint-disable-next-line react-hooks/exhaustive-deps
  useResetOnChange(keyStr, () => setStack([undefined]));

  const query = useQuery({
    queryKey: [...queryKey, { cursor: currentCursor }],
    queryFn: () => fetchPage(currentCursor),
    gcTime,
  });

  const goNext = useCallback(() => {
    if (query.data?.nextCursor) {
      setStack(prev => [...prev, query.data!.nextCursor!]);
    }
  }, [query.data]);

  const goPrev = useCallback(() => {
    setStack(prev => (prev.length > 1 ? prev.slice(0, -1) : prev));
  }, []);

  const reset = useCallback(() => setStack([undefined]), []);

  return {
    items: query.data?.items ?? [],
    isLoading: query.isLoading,
    isFetching: query.isFetching,
    isError: query.isError,
    hasNext: !!query.data?.nextCursor,
    hasPrev: stack.length > 1,
    goNext,
    goPrev,
    reset,
  };
}

import { useEffect, useRef } from "react";
function useResetOnChange(value: string, onChange: () => void) {
  const prev = useRef(value);
  useEffect(() => {
    if (prev.current !== value) {
      prev.current = value;
      onChange();
    }
  }, [value, onChange]);
}
```

- [ ] **Step 5: Run tests — expect green**

Run: `cmd //c "cd web && npm run test -- --run lib/list/__tests__/use-cursor-list"`
Expected: 3 tests PASS.

- [ ] **Step 6: Commit**

```bash
git add web/src/lib/list/types.ts \
        web/src/lib/list/useCursorList.ts \
        web/src/lib/list/__tests__/use-cursor-list.test.tsx
git commit -m "feat(web): useCursorList hook + CursorListResult types (ADR-0095)"
```

---

### Task 13: useListUrlState hook (Vitest)

**Files:**
- Create: `web/src/lib/list/useListUrlState.ts`
- Create: `web/src/lib/list/__tests__/use-list-url-state.test.tsx`

- [ ] **Step 1: Write failing tests**

Create `web/src/lib/list/__tests__/use-list-url-state.test.tsx`:

```tsx
import { describe, expect, it } from "vitest";
import { renderHook, act } from "@testing-library/react";
import { MemoryRouter, useLocation } from "react-router-dom";
import type { ReactNode } from "react";
import { useListUrlState } from "../useListUrlState";

function withRouter(initial: string) {
  return ({ children }: { children: ReactNode }) => (
    <MemoryRouter initialEntries={[initial]}>{children}</MemoryRouter>
  );
}

const config = {
  defaultSortBy: "createdAt" as const,
  defaultSortOrder: "desc" as const,
  allowedSortFields: ["createdAt", "name"] as const,
};

describe("useListUrlState", () => {
  it("falls back to defaults when URL has no params", () => {
    const { result } = renderHook(() => useListUrlState(config), { wrapper: withRouter("/") });
    expect(result.current.sortBy).toBe("createdAt");
    expect(result.current.sortOrder).toBe("desc");
  });

  it("reads sort from URL when present", () => {
    const { result } = renderHook(
      () => useListUrlState(config),
      { wrapper: withRouter("/?sortBy=name&sortOrder=asc") },
    );
    expect(result.current.sortBy).toBe("name");
    expect(result.current.sortOrder).toBe("asc");
  });

  it("falls back to default when sortBy is not in allowlist", () => {
    const { result } = renderHook(
      () => useListUrlState(config),
      { wrapper: withRouter("/?sortBy=garbage") },
    );
    expect(result.current.sortBy).toBe("createdAt");
  });

  it("setSort updates the URL", () => {
    let pathname = "";
    let search = "";
    function Inner() {
      const loc = useLocation();
      pathname = loc.pathname;
      search = loc.search;
      return null;
    }
    const { result } = renderHook(
      () => {
        const s = useListUrlState(config);
        return s;
      },
      {
        wrapper: ({ children }) => (
          <MemoryRouter initialEntries={["/"]}>
            <Inner />
            {children}
          </MemoryRouter>
        ),
      },
    );

    act(() => { result.current.setSort("name", "asc"); });

    expect(search).toContain("sortBy=name");
    expect(search).toContain("sortOrder=asc");
    expect(pathname).toBe("/");
  });
});
```

- [ ] **Step 2: Run tests — expect failure (module missing)**

Run: `cmd //c "cd web && npm run test -- --run lib/list/__tests__/use-list-url-state"`
Expected: FAIL.

- [ ] **Step 3: Implement `useListUrlState`**

Create `web/src/lib/list/useListUrlState.ts`:

```ts
import { useCallback, useMemo } from "react";
import { useSearchParams } from "react-router-dom";
import type { SortDirection } from "./types";

interface Config<TField extends string> {
  defaultSortBy: TField;
  defaultSortOrder: SortDirection;
  allowedSortFields: readonly TField[];
}

export interface ListUrlState<TField extends string> {
  sortBy: TField;
  sortOrder: SortDirection;
  setSort: (field: TField, order: SortDirection) => void;
}

/**
 * URL-backed sort state for list pages. Falls back to defaults when URL params
 * are absent or invalid (per ADR-0095 §6.1 — no error UI for "user typed garbage
 * in URL"). Cursor is intentionally not in URL — see ADR-0095 §3 Q5 = C.
 */
export function useListUrlState<TField extends string>(
  config: Config<TField>,
): ListUrlState<TField> {
  const [params, setParams] = useSearchParams();
  const allowed = useMemo(() => new Set<string>(config.allowedSortFields), [config.allowedSortFields]);

  const rawSortBy = params.get("sortBy") ?? "";
  const sortBy = allowed.has(rawSortBy) ? (rawSortBy as TField) : config.defaultSortBy;

  const rawOrder = params.get("sortOrder") ?? "";
  const sortOrder: SortDirection =
    rawOrder === "asc" || rawOrder === "desc" ? rawOrder : config.defaultSortOrder;

  const setSort = useCallback(
    (field: TField, order: SortDirection) => {
      setParams(prev => {
        const next = new URLSearchParams(prev);
        next.set("sortBy", field);
        next.set("sortOrder", order);
        return next;
      });
    },
    [setParams],
  );

  return { sortBy, sortOrder, setSort };
}
```

- [ ] **Step 4: Run tests — expect green**

Run: `cmd //c "cd web && npm run test -- --run lib/list/__tests__/use-list-url-state"`
Expected: 4 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add web/src/lib/list/useListUrlState.ts \
        web/src/lib/list/__tests__/use-list-url-state.test.tsx
git commit -m "feat(web): useListUrlState — URL-backed sort with allowlist (ADR-0095)"
```

---

### Task 14: DataTable shell components

**Files:**
- Create: `web/src/components/application/data-table/data-table.tsx`
- Create: `web/src/components/application/data-table/__tests__/sortable-head.test.tsx`
- Create: `web/src/components/application/data-table/__tests__/table-pager.test.tsx`

- [ ] **Step 1: Write failing tests for `<SortableHead>` and `<TablePager>`**

Create `web/src/components/application/data-table/__tests__/sortable-head.test.tsx`:

```tsx
import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Table } from "@/components/application/table/table";
import { SortableHead } from "../data-table";

describe("<SortableHead>", () => {
  it("renders aria-sort=none when not the active column", () => {
    render(
      <Table aria-label="t">
        <Table.Header>
          <SortableHead id="name" activeField={null} activeOrder="asc" onSortChange={() => {}}>Name</SortableHead>
        </Table.Header>
        <Table.Body>{[]}</Table.Body>
      </Table>,
    );
    expect(screen.getByRole("columnheader", { name: /name/i })).toHaveAttribute("aria-sort", "none");
  });

  it("renders aria-sort=ascending when active and asc", () => {
    render(
      <Table aria-label="t">
        <Table.Header>
          <SortableHead id="name" activeField="name" activeOrder="asc" onSortChange={() => {}}>Name</SortableHead>
        </Table.Header>
        <Table.Body>{[]}</Table.Body>
      </Table>,
    );
    expect(screen.getByRole("columnheader", { name: /name/i })).toHaveAttribute("aria-sort", "ascending");
  });

  it("clicking inactive column triggers asc; clicking active asc triggers desc; clicking active desc triggers asc", async () => {
    const onSortChange = vi.fn();
    const user = userEvent.setup();

    const { rerender } = render(
      <Table aria-label="t">
        <Table.Header>
          <SortableHead id="name" activeField={null} activeOrder="asc" onSortChange={onSortChange}>Name</SortableHead>
        </Table.Header>
        <Table.Body>{[]}</Table.Body>
      </Table>,
    );
    await user.click(screen.getByRole("columnheader", { name: /name/i }));
    expect(onSortChange).toHaveBeenLastCalledWith("name", "asc");

    rerender(
      <Table aria-label="t">
        <Table.Header>
          <SortableHead id="name" activeField="name" activeOrder="asc" onSortChange={onSortChange}>Name</SortableHead>
        </Table.Header>
        <Table.Body>{[]}</Table.Body>
      </Table>,
    );
    await user.click(screen.getByRole("columnheader", { name: /name/i }));
    expect(onSortChange).toHaveBeenLastCalledWith("name", "desc");

    rerender(
      <Table aria-label="t">
        <Table.Header>
          <SortableHead id="name" activeField="name" activeOrder="desc" onSortChange={onSortChange}>Name</SortableHead>
        </Table.Header>
        <Table.Body>{[]}</Table.Body>
      </Table>,
    );
    await user.click(screen.getByRole("columnheader", { name: /name/i }));
    expect(onSortChange).toHaveBeenLastCalledWith("name", "asc");
  });
});
```

Create `web/src/components/application/data-table/__tests__/table-pager.test.tsx`:

```tsx
import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { TablePager } from "../data-table";

describe("<TablePager>", () => {
  it("disables Prev when hasPrev=false", () => {
    render(<TablePager hasPrev={false} hasNext={true} onPrev={() => {}} onNext={() => {}} pageSize={50} />);
    expect(screen.getByRole("button", { name: /prev/i })).toBeDisabled();
  });

  it("disables Next when hasNext=false", () => {
    render(<TablePager hasPrev={true} hasNext={false} onPrev={() => {}} onNext={() => {}} pageSize={50} />);
    expect(screen.getByRole("button", { name: /next/i })).toBeDisabled();
  });

  it("calls onNext / onPrev when buttons clicked", async () => {
    const onPrev = vi.fn();
    const onNext = vi.fn();
    const user = userEvent.setup();
    render(<TablePager hasPrev={true} hasNext={true} onPrev={onPrev} onNext={onNext} pageSize={50} />);
    await user.click(screen.getByRole("button", { name: /prev/i }));
    await user.click(screen.getByRole("button", { name: /next/i }));
    expect(onPrev).toHaveBeenCalledOnce();
    expect(onNext).toHaveBeenCalledOnce();
  });
});
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cmd //c "cd web && npm run test -- --run components/application/data-table"`
Expected: FAIL (modules missing).

- [ ] **Step 3: Implement `data-table.tsx`**

Create `web/src/components/application/data-table/data-table.tsx`:

```tsx
import type { ReactNode } from "react";
import { ArrowDown, ArrowUp, ChevronSelectorVertical } from "@untitledui/icons";
import { Button } from "@/components/base/buttons/button";
import { Skeleton } from "@/components/base/skeleton/skeleton";
import { Table } from "@/components/application/table/table";
import { cx } from "@/lib/utils/cx";
import type { SortDirection } from "@/lib/list/types";

interface SortableHeadProps {
  id: string;
  activeField: string | null;
  activeOrder: SortDirection;
  onSortChange: (field: string, order: SortDirection) => void;
  isRowHeader?: boolean;
  children: ReactNode;
}

/**
 * Header cell that toggles sort on click. Toggle rule:
 *  - inactive column → asc
 *  - active asc      → desc
 *  - active desc     → asc
 */
export function SortableHead({ id, activeField, activeOrder, onSortChange, isRowHeader, children }: SortableHeadProps) {
  const isActive = activeField === id;
  const ariaSort: "ascending" | "descending" | "none" = !isActive
    ? "none"
    : activeOrder === "asc" ? "ascending" : "descending";
  const Icon = !isActive ? ChevronSelectorVertical : (activeOrder === "asc" ? ArrowUp : ArrowDown);

  const handleClick = () => {
    if (!isActive) onSortChange(id, "asc");
    else onSortChange(id, activeOrder === "asc" ? "desc" : "asc");
  };

  return (
    <Table.Head id={id} isRowHeader={isRowHeader} aria-sort={ariaSort}>
      <button
        type="button"
        onClick={handleClick}
        className={cx(
          "flex items-center gap-1 text-left text-xs font-semibold text-tertiary hover:text-primary",
          isActive && "text-primary",
        )}
      >
        <span>{children}</span>
        <Icon className="size-3.5" aria-hidden="true" />
      </button>
    </Table.Head>
  );
}

interface TablePagerProps {
  hasPrev: boolean;
  hasNext: boolean;
  onPrev: () => void;
  onNext: () => void;
  pageSize: number;
}

export function TablePager({ hasPrev, hasNext, onPrev, onNext, pageSize }: TablePagerProps) {
  return (
    <div className="flex items-center justify-between border-t border-secondary bg-primary px-6 py-3">
      <span className="text-sm text-tertiary">{pageSize} results</span>
      <div className="flex gap-2">
        <Button size="sm" color="secondary" onClick={onPrev} isDisabled={!hasPrev}>Prev</Button>
        <Button size="sm" color="secondary" onClick={onNext} isDisabled={!hasNext}>Next</Button>
      </div>
    </div>
  );
}

export function TableSkeleton({ rows = 5, cells = 2 }: { rows?: number; cells?: number }) {
  return (
    <Table.Body>
      {Array.from({ length: rows }).map((_, i) => (
        <Table.Row key={i} id={`skeleton-${i}`} data-testid="row-skeleton">
          {Array.from({ length: cells }).map((__, j) => (
            <Table.Cell key={j}><Skeleton className="h-5 w-40" /></Table.Cell>
          ))}
        </Table.Row>
      ))}
    </Table.Body>
  );
}
```

Note: confirm `<Button>`'s disabled prop name — Untitled UI buttons may use `isDisabled` (react-aria) or `disabled`. Verify with `cmd //c "grep -n 'isDisabled\|disabled' web/src/components/base/buttons/button.tsx | head"` and adjust the prop accordingly. Also verify the icon imports from `@untitledui/icons` — `ArrowUp`, `ArrowDown`, `ChevronSelectorVertical` exist (the table.tsx component already imports `ArrowDown` and `ChevronSelectorVertical`).

- [ ] **Step 4: Run tests — expect green**

Run: `cmd //c "cd web && npm run test -- --run components/application/data-table"`
Expected: 6 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add web/src/components/application/data-table/data-table.tsx \
        web/src/components/application/data-table/__tests__/sortable-head.test.tsx \
        web/src/components/application/data-table/__tests__/table-pager.test.tsx
git commit -m "feat(web): DataTable shell — SortableHead, TablePager, TableSkeleton (ADR-0095)"
```

---

## Phase F — Frontend catalog wiring

### Task 15: Regenerate OpenAPI types

**Files:**
- Modify: `web/src/api/openapi/schema.d.ts` (or wherever the generated client lives)

- [ ] **Step 1: Locate the OpenAPI codegen command**

Run: `cmd //c "cat web/package.json | grep -A 1 'openapi\|gen-types\|generate'"`

Expected output identifies a script like `"gen:openapi": "openapi-typescript ..."` or similar. Note the command.

- [ ] **Step 2: Start the API to expose the OpenAPI document**

Run in a background shell:
```bash
cmd //c "dotnet run --project src/Kartova.Api/Kartova.Api.csproj"
```

Or if a `docker compose up` setup is already in place, use that. Confirm OpenAPI is reachable at e.g. `http://localhost:5000/openapi/v1.json` (or whatever path the existing script uses).

- [ ] **Step 3: Regenerate**

Run: `cmd //c "cd web && npm run gen:openapi"` (substitute the actual script name from Step 1).

Expected: `web/src/api/openapi/schema.d.ts` updated. New types include `SortByApplications` enum (or its inlined string-literal union), a shared `SortOrder` enum, and a `CursorPage_T_` schema with the new envelope shape.

- [ ] **Step 4: Verify the types compile**

Run: `cmd //c "cd web && npm run typecheck"` (or `npx tsc --noEmit`).
Expected: TypeScript errors appear in `web/src/features/catalog/api/applications.ts` and `web/src/features/catalog/components/ApplicationsTable.tsx` because the generated `data` shape changed from `T[]` to `CursorPage<T>`. Those are the changes Task 16 + Task 17 implement.

- [ ] **Step 5: Stop the background API server**

Stop the API process started in Step 2.

- [ ] **Step 6: Commit (regen-only)**

```bash
git add web/src/api/openapi/schema.d.ts
git commit -m "chore(web): regenerate OpenAPI types — sortBy/sortOrder/cursor + CursorPage envelope"
```

(Path may differ; substitute the actual generated file's path.)

---

### Task 16: applications.ts hook + key parameterization

**Files:**
- Modify: `web/src/features/catalog/api/applications.ts`

- [ ] **Step 1: Replace the file contents**

```ts
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { apiClient } from "./client";
import { useCursorList } from "@/lib/list/useCursorList";
import type { CursorPageEnvelope } from "@/lib/list/types";
import type { RegisterApplicationInput } from "../schemas/registerApplication";

type ApplicationsListParams = {
  sortBy: "createdAt" | "name";
  sortOrder: "asc" | "desc";
  limit?: number;
};

export const applicationKeys = {
  all: ["applications"] as const,
  list: (params?: ApplicationsListParams) =>
    params
      ? ([...applicationKeys.all, "list", params] as const)
      : ([...applicationKeys.all, "list"] as const),
  detail: (id: string) => [...applicationKeys.all, "detail", id] as const,
};

export function useApplicationsList(params: ApplicationsListParams) {
  return useCursorList({
    queryKey: applicationKeys.list(params),
    fetchPage: async (cursor) => {
      const { data, error } = await apiClient.GET("/api/v1/catalog/applications", {
        params: {
          query: {
            sortBy: params.sortBy,
            sortOrder: params.sortOrder,
            limit: params.limit ?? 50,
            cursor,
          },
        },
      });
      if (error) throw error;
      if (!data) throw new Error("API returned neither data nor error");
      return data as CursorPageEnvelope<NonNullable<typeof data>["items"][number]>;
    },
  });
}

export function useApplication(id: string) {
  return useQuery({
    queryKey: applicationKeys.detail(id),
    enabled: id !== "",
    queryFn: async () => {
      const { data, error } = await apiClient.GET(
        "/api/v1/catalog/applications/{id}",
        { params: { path: { id } } }
      );
      if (error) throw error;
      if (!data) throw new Error("API returned neither data nor error");
      return data;
    },
  });
}

export function useRegisterApplication() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (input: RegisterApplicationInput) => {
      const { data, error } = await apiClient.POST("/api/v1/catalog/applications", { body: input });
      if (error) throw error;
      if (!data) throw new Error("API returned neither data nor error");
      return data;
    },
    onSuccess: () => {
      // Invalidate the list prefix — covers all parameterized queryKeys.
      qc.invalidateQueries({ queryKey: applicationKeys.all });
    },
  });
}
```

- [ ] **Step 2: Typecheck**

Run: `cmd //c "cd web && npm run typecheck"`
Expected: errors only in `ApplicationsTable.tsx` and `CatalogListPage.tsx` (Task 17 fixes them).

- [ ] **Step 3: No commit yet — combine with Task 17**

The wire-shape change spans both files.

---

### Task 17: ApplicationsTable + CatalogListPage rewire

**Files:**
- Modify: `web/src/features/catalog/components/ApplicationsTable.tsx`
- Modify: `web/src/features/catalog/pages/CatalogListPage.tsx`

- [ ] **Step 1: Rewrite `ApplicationsTable.tsx`**

```tsx
import { Link } from "react-router-dom";
import { Table } from "@/components/application/table/table";
import { Card, CardContent } from "@/components/base/card/card";
import { SortableHead, TablePager, TableSkeleton } from "@/components/application/data-table/data-table";
import type { CursorListResult } from "@/lib/list/types";

export interface ApplicationRow {
  id: string;
  name: string;
  displayName: string;
  description: string;
  ownerUserId?: string;
  createdAt?: string;
}

type SortField = "createdAt" | "name";
type SortDir = "asc" | "desc";

interface Props {
  list: CursorListResult<ApplicationRow>;
  sortBy: SortField;
  sortOrder: SortDir;
  onSortChange: (field: SortField, order: SortDir) => void;
}

export function ApplicationsTable({ list, sortBy, sortOrder, onSortChange }: Props) {
  if (list.isLoading) {
    return (
      <Table aria-label="Applications">
        <Table.Header>
          <Table.Head id="name" isRowHeader>Name</Table.Head>
          <Table.Head id="description">Description</Table.Head>
        </Table.Header>
        <TableSkeleton rows={5} cells={2} />
      </Table>
    );
  }

  if (list.items.length === 0) {
    return (
      <Card className="mx-auto max-w-md text-center">
        <CardContent className="space-y-2 p-8">
          <p className="text-base font-medium text-primary">No applications yet</p>
          <p className="text-sm text-tertiary">
            Use the &quot;+ Register Application&quot; button in the header to add your first one.
          </p>
        </CardContent>
      </Card>
    );
  }

  return (
    <div className="overflow-hidden rounded-xl bg-primary shadow-xs ring-1 ring-secondary">
      <Table aria-label="Applications">
        <Table.Header>
          <SortableHead
            id="name"
            isRowHeader
            activeField={sortBy}
            activeOrder={sortOrder}
            onSortChange={(f, o) => onSortChange(f as SortField, o)}
          >
            Name
          </SortableHead>
          <Table.Head id="description">Description</Table.Head>
          <SortableHead
            id="createdAt"
            activeField={sortBy}
            activeOrder={sortOrder}
            onSortChange={(f, o) => onSortChange(f as SortField, o)}
          >
            Created
          </SortableHead>
        </Table.Header>
        <Table.Body>
          {list.items.map(app => (
            <Table.Row key={app.id} id={app.id}>
              <Table.Cell>
                <Link
                  to={`/catalog/applications/${app.id}`}
                  className="block font-medium text-primary hover:underline"
                >
                  {app.displayName}
                </Link>
                <span className="font-mono text-xs text-tertiary">{app.name}</span>
              </Table.Cell>
              <Table.Cell className="text-sm text-tertiary">
                {app.description || <span className="italic">No description</span>}
              </Table.Cell>
              <Table.Cell className="text-sm text-tertiary">
                {app.createdAt ? new Date(app.createdAt).toLocaleDateString() : ""}
              </Table.Cell>
            </Table.Row>
          ))}
        </Table.Body>
      </Table>
      <TablePager
        hasPrev={list.hasPrev}
        hasNext={list.hasNext}
        onPrev={list.goPrev}
        onNext={list.goNext}
        pageSize={list.items.length}
      />
    </div>
  );
}
```

- [ ] **Step 2: Rewrite `CatalogListPage.tsx`**

```tsx
import { useState } from "react";
import { Plus } from "@untitledui/icons";
import { Button } from "@/components/base/buttons/button";
import { Card, CardContent } from "@/components/base/card/card";
import { useApplicationsList } from "@/features/catalog/api/applications";
import { useListUrlState } from "@/lib/list/useListUrlState";
import { ApplicationsTable } from "@/features/catalog/components/ApplicationsTable";
import { RegisterApplicationDialog } from "@/features/catalog/components/RegisterApplicationDialog";

const ALLOWED_SORT_FIELDS = ["createdAt", "name"] as const;

export function CatalogListPage() {
  const { sortBy, sortOrder, setSort } = useListUrlState({
    defaultSortBy: "createdAt",
    defaultSortOrder: "desc",
    allowedSortFields: ALLOWED_SORT_FIELDS,
  });

  const list = useApplicationsList({ sortBy, sortOrder });
  const [dialogOpen, setDialogOpen] = useState(false);

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <h2 className="text-2xl font-semibold text-primary">Catalog</h2>
        <Button onClick={() => setDialogOpen(true)} size="sm" color="primary" iconLeading={Plus}>
          Register Application
        </Button>
      </div>

      {list.isError ? (
        <Card className="mx-auto max-w-md">
          <CardContent className="space-y-2 p-6 text-center">
            <p className="text-base font-medium text-error-primary">Failed to load applications</p>
            <p className="text-sm text-tertiary">Try again in a moment, or check that you're signed in.</p>
          </CardContent>
        </Card>
      ) : (
        <ApplicationsTable
          list={list}
          sortBy={sortBy}
          sortOrder={sortOrder}
          onSortChange={setSort}
        />
      )}

      <RegisterApplicationDialog open={dialogOpen} onOpenChange={setDialogOpen} />
    </div>
  );
}
```

- [ ] **Step 3: Typecheck + frontend tests**

Run: `cmd //c "cd web && npm run typecheck && npm run test -- --run"`
Expected: typecheck clean, all Vitest tests PASS (existing + Tasks 12–14 new).

- [ ] **Step 4: Commit (Tasks 16+17 combined)**

```bash
git add web/src/features/catalog/api/applications.ts \
        web/src/features/catalog/components/ApplicationsTable.tsx \
        web/src/features/catalog/pages/CatalogListPage.tsx
git commit -m "feat(catalog/web): wire useApplicationsList + sortable headers + Prev/Next pager (ADR-0095)"
```

---

## Phase G — Dev seed, smoke, ADR flip

### Task 18: Dev seed — bump to ~120 applications for Org A

**Files:**
- Modify: `src/Kartova.Migrator/DevSeed.cs`

- [ ] **Step 1: Append application seeding to `DevSeed.RunAsync`**

After the existing `organizations` insert block (inside the same `RunAsync` method), append:

```csharp
// Pagination requires a non-trivial fixture to be exercisable in `docker compose up`
// (ADR-0095 §10). Seed ~120 applications for Org A with deterministic varied names so
// sort-by-name and sort-by-createdAt produce visibly different orderings.
await ExecAsync(conn, "ALTER TABLE applications NO FORCE ROW LEVEL SECURITY;");
try
{
    await using var checkCmd = conn.CreateCommand();
    checkCmd.CommandText = "SELECT COUNT(*) FROM applications WHERE tenant_id = $1;";
    checkCmd.Parameters.AddWithValue(OrgATenantId);
    var existing = (long?)await checkCmd.ExecuteScalarAsync() ?? 0L;

    if (existing == 0L)
    {
        // Names chosen so alphabetical and chronological orders diverge.
        var origin = DateTimeOffset.UtcNow.AddMinutes(-120);
        for (var i = 0; i < 120; i++)
        {
            await using var insertCmd = conn.CreateCommand();
            insertCmd.CommandText = """
                INSERT INTO applications (id, tenant_id, name, display_name, description, owner_user_id, created_at)
                VALUES (gen_random_uuid(), $1, $2, $3, $4, gen_random_uuid(), $5);
                """;
            // Reverse-alphabetical name relative to insertion order so name-asc != createdAt-asc.
            var letter = (char)('a' + ((119 - i) % 26));
            var name = $"{letter}-app-{i:D3}";
            var displayName = char.ToUpper(letter) + $" App {i:D3}";
            insertCmd.Parameters.AddWithValue(OrgATenantId);
            insertCmd.Parameters.AddWithValue(name);
            insertCmd.Parameters.AddWithValue(displayName);
            insertCmd.Parameters.AddWithValue($"Seeded application #{i + 1}");
            insertCmd.Parameters.AddWithValue(origin.AddMinutes(i));
            await insertCmd.ExecuteNonQueryAsync();
        }
        logger.LogInformation("Dev seed: inserted 120 applications for Org A.");
    }
    else
    {
        logger.LogInformation("Dev seed: applications already present (Count={Count}).", existing);
    }
}
finally
{
    await ExecAsync(conn, "ALTER TABLE applications FORCE ROW LEVEL SECURITY;");
}
```

Verify the column names match the actual schema (run `cmd //c "grep -n 'name\|display_name\|owner_user_id\|created_at' src/Modules/Catalog/Kartova.Catalog.Infrastructure/Migrations/*Application*.cs | head"`). Adjust if migration uses different snake_case mappings.

- [ ] **Step 2: Build green check**

Run: `cmd //c "dotnet build src/Kartova.Migrator/Kartova.Migrator.csproj --tl:off"`
Expected: 0 warnings, 0 errors.

- [ ] **Step 3: Run the migrator (against a clean dev DB)**

Run via `docker compose down -v && docker compose up -d postgres && docker compose run --rm migrator` (or whatever the dev workflow is).
Expected log line: `Dev seed: inserted 120 applications for Org A.`

If running the migrator standalone is awkward locally, defer this verification to the smoke-test in Task 19 (the seed runs as part of the Compose pre-up hook).

- [ ] **Step 4: Commit**

```bash
git add src/Kartova.Migrator/DevSeed.cs
git commit -m "chore(migrator): seed 120 applications for Org A (pagination smoke fixture, ADR-0095)"
```

---

### Task 19: Playwright smoke E2E

**Files:**
- (No new files; smoke runs via Playwright MCP, not committed code.)

- [ ] **Step 1: Cold-start dev server**

Stop any running `npm run dev` / `docker compose` instance. Run a fresh: `cmd //c "docker compose up -d"` for the API + DB, then `cmd //c "cd web && npm run dev"` in a separate background shell.

Wait for both to report ready (API health endpoint OK, Vite "ready in NNN ms").

- [ ] **Step 2: Drive the Playwright MCP scenario**

Using `mcp__playwright__browser_navigate` and friends, execute:

1. Navigate to `http://localhost:5173/catalog` (or whatever the dev URL is). Sign in via the dev OIDC flow if a login page intercepts.
2. Wait for the table to render. Snapshot.
3. Assert the first row's createdAt is the most-recent (default `createdAt:desc`).
4. Click the **Name** column header.
5. Assert the URL becomes `/catalog?sortBy=name&sortOrder=asc`.
6. Snapshot — first row's name should now start with `A`.
7. Click **Next**. Assert page contents change. Assert **Prev** is now enabled.
8. Click **Prev**. Assert original first-page contents return.
9. Check `mcp__playwright__browser_console_messages` — no errors.

- [ ] **Step 3: Capture evidence**

Save Playwright screenshots to `docs/superpowers/evidence/2026-05-04-sorting-pagination/` (mirroring the Untitled UI evidence convention). Files: `default-sort.png`, `sort-name-asc.png`, `page-2.png`, `page-1-back.png`.

- [ ] **Step 4: Stop dev servers**

Stop both `npm run dev` and `docker compose`.

- [ ] **Step 5: Commit evidence**

```bash
git add docs/superpowers/evidence/2026-05-04-sorting-pagination/
git commit -m "docs(evidence): Playwright smoke for sorting + pagination"
```

If the smoke cannot be run locally (Docker unavailable), do **not** mark this task complete. Note explicitly in the slice review that Task 19 is *pending user verification*.

---

### Task 20: ADR-0095 flip to Accepted, README chronology, CHECKLIST

**Files:**
- Modify: `docs/architecture/decisions/ADR-0095-cursor-pagination-contract.md`
- Modify: `docs/architecture/decisions/README.md`
- Modify: `docs/architecture/decisions/ADR-0029-rest-as-primary-api-style.md`
- Modify: `docs/product/CHECKLIST.md`

- [ ] **Step 1: Flip ADR-0095 status**

In `docs/architecture/decisions/ADR-0095-cursor-pagination-contract.md`, change `**Status:** Proposed` to `**Status:** Accepted`.

- [ ] **Step 2: Update README index status**

In `docs/architecture/decisions/README.md`, change the ADR-0095 row's status column from `Proposed` to `Accepted`. Update the chronology entry to:

```markdown
| 2026-05-04 | ADR-0095 (Cursor pagination contract) accepted — concrete contract for ADR-0029's "pagination via cursors" mention; first-cut mandate + arch fitness rule; reference impl on Applications list |
```

- [ ] **Step 3: Cross-link from ADR-0029**

In `docs/architecture/decisions/ADR-0029-rest-as-primary-api-style.md`, append (or extend the Related line) so future readers find ADR-0095:

```markdown
**See also:** ADR-0095 (Cursor pagination contract) — concretizes the "pagination via cursors" mention in §Decision.
```

- [ ] **Step 4: Tick the CHECKLIST**

In `docs/product/CHECKLIST.md`, find the relevant phase / story (likely E-02 "Catalog: list" or a Phase 1 cross-cutting item) and either tick the existing pagination item or append:

```markdown
- [x] **Cross-cutting: cursor-pagination contract.** ADR-0095 + reference impl on Applications list. (2026-05-04)
```

- [ ] **Step 5: Final solution build + full test sweep**

Run all in sequence:

```bash
cmd //c "dotnet build Kartova.slnx --tl:off"
cmd //c "dotnet test Kartova.slnx --tl:off"
cmd //c "cd web && npm run typecheck && npm run test -- --run && npm run lint"
```

Expected: 0 warnings, 0 errors, all tests pass.

- [ ] **Step 6: Commit**

```bash
git add docs/architecture/decisions/ADR-0095-cursor-pagination-contract.md \
        docs/architecture/decisions/README.md \
        docs/architecture/decisions/ADR-0029-rest-as-primary-api-style.md \
        docs/product/CHECKLIST.md
git commit -m "docs: ADR-0095 → Accepted; ADR-0029 cross-link; CHECKLIST tick (sorting+pagination slice)"
```

---

## Slice DoD checklist (from CLAUDE.md)

After all tasks pass, the slice is complete only when ALL of the following are green and citable:

1. **Full solution build with `TreatWarningsAsErrors=true`** — 0 warnings, 0 errors.
   `cmd //c "dotnet build Kartova.slnx --tl:off"`
2. **Per-task subagent reviews** (spec-compliance + code-quality) executed during subagent-driven execution. Never skipped.
3. **Slice-level `superpowers:requesting-code-review`** against the **full branch diff** with this plan + the design spec as context.
4. **Full test suite green:**
   - `cmd //c "dotnet test Kartova.slnx --tl:off"` — unit + arch + integration (Testcontainers).
   - `cmd //c "cd web && npm run test -- --run"` — Vitest.
5. **`docker compose up` smoke evidence** (this slice qualifies — it changes wire shape and adds query-string binding):
   - `curl '/api/v1/catalog/applications?sortBy=name&limit=10'` returns the new envelope. Capture output.
   - `curl '/api/v1/catalog/applications?sortBy=garbage'` returns 400 RFC 7807. Capture output.
   - One Playwright session (Task 19) confirms the UI behavior end-to-end.

If Docker is unavailable on this machine, mark step 5 as *pending user verification* explicitly. Never imply completion without it.

---

## Self-review (post-write)

**Spec coverage:** every section of `2026-05-04-sorting-pagination-design.md` maps to a task —
- §4 API contract → Tasks 2, 3, 4, 5, 8, 9
- §5 Server-side architecture → Tasks 2–7, 8, 9
- §6 Frontend architecture → Tasks 12–17
- §7 Standards → Task 1, Task 11, Task 20
- §8 Architecture fitness → Task 11
- §9 Testing — all five tiers → Tasks 3 (codec unit), 5 (extension unit), 6 (handler unit), 10 (integration), 11 (architecture), 12–14 (frontend unit), 19 (E2E smoke)
- §10 Dev seed → Task 18
- §11 ADR work → Tasks 1, 20
- §12 DoD → embedded in slice DoD checklist above
- §13 Out-of-scope → respected (no multi-field sort, no `?include=total`, no infinite scroll, no server-emitted prev cursor)

**Placeholder scan:** no `TBD`/`TODO`/"add appropriate error handling"/"similar to Task N"; every code block is complete; every command is exact.

**Type consistency:** `CursorPage<T>` properties (`Items`, `NextCursor`, `PrevCursor`) consistent across SharedKernel, handler, endpoint, OpenAPI envelope, frontend `CursorPageEnvelope`. `SortOrder` enum (`Asc`/`Desc`) consistent backend; mapped to lowercase string `asc`/`desc` on the wire (cursor `d` field; `?sortOrder=...` query param). `ApplicationSortField` enum (`CreatedAt`/`Name`) mapped to lowercase `createdAt`/`name` strings on the wire via the OpenAPI codegen and the `ApplicationSortSpecs` `FieldName` values.
