using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Kartova.Organization.Contracts;

/// <param name="Token">The single-use invitation token from the email link.</param>
/// <param name="Password">SENSITIVE — must never be logged, traced, or included in diagnostic output.</param>
/// <param name="DisplayName">The display name the invitee chooses.</param>
[ExcludeFromCodeCoverage]
public sealed record AcceptInvitationRequest(
    string Token,
    string Password,
    string DisplayName)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Token = ").Append(Token);
        builder.Append(", Password = ***");
        builder.Append(", DisplayName = ").Append(DisplayName);
        return true;
    }
}
