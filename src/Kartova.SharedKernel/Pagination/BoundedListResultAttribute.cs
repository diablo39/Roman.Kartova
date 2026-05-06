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
