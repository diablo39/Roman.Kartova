namespace Kartova.SharedKernel.Identity;

public enum KeycloakAdminError
{
    EmailAlreadyExists,
    Unauthorized,
    NotFound,
    Unexpected,
}

public sealed class KeycloakAdminException : Exception
{
    public KeycloakAdminError Error { get; }

    public KeycloakAdminException(KeycloakAdminError error, string message) : base(message)
    {
        Error = error;
    }
}
