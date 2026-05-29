namespace Kartova.SharedKernel.Identity;

/// <summary>
/// Cross-module port over the KeyCloak Admin REST API. The production
/// implementation (<c>KeycloakAdminClient</c>) issues authenticated HTTPS
/// calls against the realm's <c>/admin/realms/{realm}/...</c> endpoints
/// using a service-account access token; tests substitute the interface
/// with NSubstitute so handler-level logic can be exercised without a
/// running KeyCloak. ADR-0098 §6.7 + slice-9 spec §6.7 (CreateInvitation
/// three-way conflict model).
///
/// <para>
/// Error contract: every non-2xx response from KeyCloak is translated into a
/// typed <see cref="KeycloakAdminException"/> whose <see cref="KeycloakAdminException.Error"/>
/// indicates the failure category. Callers MUST NOT catch plain
/// <see cref="System.Net.Http.HttpRequestException"/> or
/// <see cref="System.Exception"/> — the implementation guarantees those are
/// already mapped. Network failures (DNS, TLS, socket reset) surface as
/// <see cref="KeycloakAdminError.Unexpected"/>; callers that need to map
/// them to ProblemDetails should choose
/// <c>ProblemTypes.ServiceUnavailable</c> (502).
/// </para>
///
/// <para>
/// Idempotency: <see cref="DeleteUserAsync"/> throws on 404 rather than
/// returning silently — pair it with a try/catch on
/// <see cref="KeycloakAdminError.NotFound"/> when invoking from
/// compensating workflows (revoke / expire cleanup) where the user may
/// already be gone from a prior run.
/// </para>
/// </summary>
public interface IKeycloakAdminClient
{
    /// <summary>
    /// Creates a new realm user in KeyCloak.
    /// </summary>
    /// <param name="request">User attributes (email, optional name parts,
    /// tenant id attribute, required-action list). The Email field is also
    /// used as the username because the realm does not assume
    /// <c>registrationEmailAsUsername=true</c>.</param>
    /// <param name="ct">Cancellation token tied to the request lifetime.</param>
    /// <returns>The newly-assigned KeyCloak user id (parsed from the
    /// <c>Location</c> response header).</returns>
    /// <exception cref="KeycloakAdminException">
    /// Thrown with <see cref="KeycloakAdminError.EmailAlreadyExists"/> on
    /// HTTP 409 (realm already has a user with that email — the platform-
    /// wide third leg of the slice-9 §6.7 conflict matrix);
    /// <see cref="KeycloakAdminError.Unauthorized"/> on HTTP 401 / 403
    /// (admin client credentials missing or insufficient);
    /// <see cref="KeycloakAdminError.Unexpected"/> for any other non-2xx
    /// response (including a missing or malformed Location header).
    /// </exception>
    Task<Guid> CreateUserAsync(CreateKeycloakUserRequest request, CancellationToken ct);

    /// <summary>
    /// Fetches a single realm user by id.
    /// </summary>
    /// <param name="userId">KeyCloak user id.</param>
    /// <param name="ct">Cancellation token tied to the request lifetime.</param>
    /// <returns>The user's <see cref="KeycloakUser"/> when found; <c>null</c>
    /// on HTTP 404 (treated as a normal "not found" outcome rather than an
    /// exceptional condition). Callers should branch on null instead of
    /// catching <see cref="KeycloakAdminError.NotFound"/>.</returns>
    /// <exception cref="KeycloakAdminException">
    /// Thrown with <see cref="KeycloakAdminError.Unexpected"/> for any
    /// non-2xx response other than 404 (including an empty / malformed JSON
    /// body on 200).
    /// </exception>
    Task<KeycloakUser?> GetUserAsync(Guid userId, CancellationToken ct);

    /// <summary>
    /// Assigns a realm role to a user. The implementation first resolves the
    /// role by name (so the wire payload carries the role's GUID) and then
    /// POSTs the assignment.
    /// </summary>
    /// <param name="userId">KeyCloak user id.</param>
    /// <param name="roleName">Realm role name (e.g. <c>Member</c> / <c>Admin</c>
    /// — see <c>KartovaRoles</c> for the canonical allow-list).</param>
    /// <param name="ct">Cancellation token tied to the request lifetime.</param>
    /// <exception cref="KeycloakAdminException">
    /// Thrown with <see cref="KeycloakAdminError.NotFound"/> on HTTP 404
    /// from EITHER the role-lookup step (role name unknown) OR the
    /// assignment step (user no longer exists — possible if a concurrent
    /// delete races the assignment); callers cannot distinguish without
    /// inspecting the inner message. The slice-9 CreateInvitation handler
    /// (spec §6.7) treats both as a compensating failure: the orphan KC
    /// user is best-effort deleted and a 502 surfaces upstream.
    /// <see cref="KeycloakAdminError.Unexpected"/> for any other non-2xx
    /// response.
    /// </exception>
    Task AssignRealmRoleAsync(Guid userId, string roleName, CancellationToken ct);

    /// <summary>
    /// Searches realm users using KeyCloak's free-text <c>search=</c> parameter
    /// (matches against username / email / first / last name).
    /// </summary>
    /// <param name="query">Search string passed verbatim to KeyCloak after
    /// URL-encoding.</param>
    /// <param name="limit">Maximum number of results (<c>max=</c> parameter).</param>
    /// <param name="ct">Cancellation token tied to the request lifetime.</param>
    /// <returns>The matching users in KeyCloak's response order. Returns an
    /// empty list when no user matches — never null.</returns>
    /// <exception cref="KeycloakAdminException">
    /// Thrown with <see cref="KeycloakAdminError.Unexpected"/> for any
    /// non-2xx response.
    /// </exception>
    Task<IReadOnlyList<KeycloakUser>> SearchUsersAsync(string query, int limit, CancellationToken ct);

    /// <summary>
    /// Deletes a realm user.
    /// </summary>
    /// <param name="userId">KeyCloak user id.</param>
    /// <param name="ct">Cancellation token tied to the request lifetime.</param>
    /// <exception cref="KeycloakAdminException">
    /// Thrown with <see cref="KeycloakAdminError.NotFound"/> on HTTP 404
    /// (user already gone). Callers invoking this from compensating
    /// cleanup paths (e.g. revoke / expire invitation, or the orphan-KC
    /// branch of CreateInvitation) SHOULD wrap the call in a try/catch and
    /// swallow <see cref="KeycloakAdminError.NotFound"/> — the desired end
    /// state ("user does not exist") already matches.
    /// <see cref="KeycloakAdminError.Unexpected"/> for any other non-2xx
    /// response.
    /// </exception>
    Task DeleteUserAsync(Guid userId, CancellationToken ct);
}
