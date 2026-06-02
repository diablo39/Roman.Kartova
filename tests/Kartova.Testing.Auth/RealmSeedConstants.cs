using System.Diagnostics.CodeAnalysis;

namespace Kartova.Testing.Auth;

/// <summary>
/// Single source of truth for the Keycloak realm-seed values used by every
/// integration-test fixture that boots <c>kartova-realm.json</c> in a
/// Testcontainer. Must stay in lockstep with
/// <c>deploy/keycloak/kartova-realm.json</c> — any rename there must also
/// land here, otherwise host startup fails inside
/// <c>AddKeycloakAdminClient.ValidateOnStart</c> with a confusing
/// "admin client can't authenticate" surface error.
/// <para>
/// Slice 9 / H1-prereq: extracted out of <see cref="KartovaApiFixtureBase"/>
/// and the per-test-class env-var blocks in <c>Kartova.Api.IntegrationTests</c>
/// so future H1 follow-ups (notably the planned
/// <c>Kartova.SharedKernel.Identity.IntegrationTests</c> project that drives
/// <c>KeycloakAdminClient</c> directly) reuse the same literals.
/// </para>
/// </summary>
[ExcludeFromCodeCoverage]
public static class RealmSeedConstants
{
    /// <summary>
    /// Realm name imported by <c>--import-realm</c>. Matches the <c>realm</c>
    /// field at the top of <c>kartova-realm.json</c>.
    /// </summary>
    public const string RealmName = "kartova";

    /// <summary>
    /// Service-account client used by the API to call the Keycloak admin REST API.
    /// Matches the <c>clientId</c> of the admin client in <c>kartova-realm.json</c>.
    /// </summary>
    public const string AdminClientId = "kartova-admin";

    /// <summary>
    /// Client-secret for <see cref="AdminClientId"/>. Test-only — production
    /// overrides this via the deployed secret store, but the realm-seed JSON
    /// pins it so the Testcontainer boots predictably.
    /// </summary>
    public const string AdminClientSecret = "admin-dev-secret";
}
