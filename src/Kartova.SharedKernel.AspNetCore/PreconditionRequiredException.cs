namespace Kartova.SharedKernel.AspNetCore;

/// <summary>
/// Thrown by <see cref="IfMatchEndpointFilter"/> when the <c>If-Match</c>
/// header is absent or malformed. Mapped to RFC 7807 <c>428 Precondition
/// Required</c> by <see cref="PreconditionRequiredExceptionHandler"/>.
/// </summary>
public sealed class PreconditionRequiredException(string message) : Exception(message);
