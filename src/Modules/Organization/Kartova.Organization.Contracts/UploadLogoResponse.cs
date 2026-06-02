using System.Diagnostics.CodeAnalysis;

namespace Kartova.Organization.Contracts;

/// <summary>
/// Response shape for <c>PUT /api/v1/organizations/me/logo</c> — surfaces the
/// content-addressed ETag (SHA-256 hex of the stored bytes) plus the negotiated
/// MIME type after server-side magic-byte validation + SVG sanitization
/// (slice-9 spec §6.4). Clients use <c>LogoEtag</c> as the cache-busting query
/// parameter when fetching the logo via <c>GET /me/logo</c>.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed record UploadLogoResponse(string LogoEtag, string MimeType);
