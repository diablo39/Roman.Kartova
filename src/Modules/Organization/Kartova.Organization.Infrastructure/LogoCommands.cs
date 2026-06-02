using System.Text;
using Kartova.Organization.Application;
using Kartova.Organization.Domain;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Organization.Infrastructure;

/// <summary>
/// Organization-logo write/read handler (slice-9 spec §6.4). Centralizes the
/// upload/clear/serve workflow so endpoint delegates stay thin: magic-byte
/// validation + SVG sanitization run here (delegating to
/// <see cref="LogoValidation"/>), then the domain aggregate's
/// <c>SetLogo</c>/<c>ClearLogo</c> methods enforce mime allow-list and size
/// invariants. Lives in Infrastructure (not Application) because it depends on
/// <see cref="OrganizationDbContext"/> — same placement as
/// <c>UpdateOrgProfileHandler</c> / <c>OrgProfileQueries</c>.
/// </summary>
public sealed class LogoCommands
{
    private readonly OrganizationDbContext _db;

    public LogoCommands(OrganizationDbContext db)
    {
        _db = db;
    }

    public async Task<UploadLogoResult> UploadAsync(byte[] bytes, string mimeType, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(mimeType);

        if (!LogoValidation.MagicBytesMatch(bytes, mimeType))
        {
            return new UploadLogoResult.Rejected("magic-byte mismatch");
        }

        // Load the aggregate BEFORE the SVG sanitizer runs. On a misconfigured
        // tenant with no Organization row, returning NotFound here avoids
        // allocating a string for, and running Ganss.Xss over, attacker-supplied
        // bytes that we have nowhere to persist anyway — smaller attack surface
        // and no wasted CPU on hostile payloads.
        var org = await _db.Organizations.FirstOrDefaultAsync(ct);
        if (org is null)
        {
            return new UploadLogoResult.NotFound();
        }

        var processed = bytes;
        if (mimeType == "image/svg+xml")
        {
            // Encoding.UTF8.GetString is isolated to the SVG branch because the
            // sanitizer (Ganss.Xss) requires a string. PNG/JPEG never decode.
            var (clean, materiallyChanged) = LogoValidation.SanitizeSvg(Encoding.UTF8.GetString(bytes));
            if (materiallyChanged)
            {
                return new UploadLogoResult.Rejected("SVG contained disallowed content");
            }
            processed = Encoding.UTF8.GetBytes(clean);
        }

        org.SetLogo(OrgLogo.Create(processed, mimeType));
        await _db.SaveChangesAsync(ct);
        return new UploadLogoResult.Accepted(org.Logo!.ContentHash, mimeType);
    }

    public async Task<bool> ClearAsync(CancellationToken ct)
    {
        var org = await _db.Organizations.FirstOrDefaultAsync(ct);
        if (org is null) return false;
        org.ClearLogo();
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<LogoServeData?> GetServeDataAsync(CancellationToken ct)
    {
        var row = await _db.Organizations
            .AsNoTracking()
            .Where(o => o.Logo != null)
            .Select(o => new LogoServeData(o.Logo!.Bytes, o.Logo.MimeType, o.Logo.ContentHash))
            .FirstOrDefaultAsync(ct);
        return row;
    }
}

/// <summary>
/// Outcome of <see cref="LogoCommands.UploadAsync"/>. Co-located with the
/// handler — same convention as <c>UpdateOrgProfileResult</c> in D2.
/// </summary>
public abstract record UploadLogoResult
{
    private UploadLogoResult() { }

    public sealed record Accepted(string Etag, string MimeType) : UploadLogoResult;
    public sealed record Rejected(string Reason) : UploadLogoResult;
    public sealed record NotFound : UploadLogoResult;
}

/// <summary>
/// Carrier for <see cref="LogoCommands.GetServeDataAsync"/>: the bytes the
/// endpoint streams back, plus the MIME type for <c>Content-Type</c> and the
/// SHA-256 content hash used as the strong ETag.
/// </summary>
public sealed record LogoServeData(byte[] Bytes, string MimeType, string ContentHash);
