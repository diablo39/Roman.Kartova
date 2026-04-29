namespace Kartova.SharedKernel.AspNetCore;

/// <summary>
/// Exposes the current authenticated user's identity from the request context.
/// Caller must run inside the auth pipeline — accessing properties when no user
/// is authenticated throws.
/// </summary>
public interface ICurrentUser
{
    /// <summary>
    /// Guid form of the JWT 'sub' claim. KeyCloak issues UUIDs for user IDs.
    /// </summary>
    Guid UserId { get; }
}
