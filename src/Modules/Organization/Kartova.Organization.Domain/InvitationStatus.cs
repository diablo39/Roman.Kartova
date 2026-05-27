namespace Kartova.Organization.Domain;

public enum InvitationStatus : byte
{
    Pending = 1,
    Accepted = 2,
    Revoked = 3,
    Expired = 4,
}
