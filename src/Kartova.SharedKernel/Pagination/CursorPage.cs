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
