namespace Kartova.SharedKernel.Postgres.Pagination;

/// <summary>
/// Escapes LIKE/ILIKE metacharacters so user input matches literally under
/// <c>ESCAPE '\'</c>. Shared by every list handler that does a contains filter
/// (Teams, Catalog Services/Applications) — one tested implementation.
/// Backslash is escaped first, so the escapes added for <c>%</c> and <c>_</c>
/// are not themselves re-escaped.
/// </summary>
public static class LikeEscaping
{
    public static string EscapeLike(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        return raw.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
    }
}
