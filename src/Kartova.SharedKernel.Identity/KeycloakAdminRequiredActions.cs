namespace Kartova.SharedKernel.Identity;

/// <summary>
/// Canonical names for KeyCloak's <c>requiredActions</c> list as accepted by the
/// admin REST API on user create/update. KeyCloak treats these as string keys
/// (an unrecognized value is silently ignored), so promoting them out of inline
/// literals avoids typos and documents which actions Kartova depends on.
/// </summary>
/// <remarks>
/// The set is intentionally narrow — only the actions currently used (or known to
/// be used in a near-term slice) appear here. Extend as new actions are wired in
/// by the calling handlers; do NOT mirror every KeyCloak-defined action up front.
/// </remarks>
public static class KeycloakAdminRequiredActions
{
    /// <summary>
    /// Forces the user to set a new password on first login. Used by the
    /// slice-9 invitation flow (spec §9.2 step 4 / Decision §14): the invited
    /// KeyCloak account is created with this required-action so the invitee
    /// completes account setup themselves rather than the OrgAdmin choosing
    /// an initial password.
    /// </summary>
    public const string UpdatePassword = "UPDATE_PASSWORD";

    /// <summary>
    /// Forces the user to confirm receipt of an email before the account
    /// becomes fully usable. NOT wired in slice 9 — Kartova has no SMTP in
    /// scope yet, so prompting the user to verify an email KeyCloak cannot
    /// send would create a dead-end UX (Decision §14). Documented here as the
    /// contract value E-06a's notification slice will reach for once SMTP
    /// infrastructure lands; included now so adopters use the same constant
    /// rather than reintroducing the string literal.
    /// </summary>
    public const string VerifyEmail = "VERIFY_EMAIL";
}
