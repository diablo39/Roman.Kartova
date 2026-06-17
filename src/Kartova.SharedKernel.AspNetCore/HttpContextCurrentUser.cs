using System.Security.Claims;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.AspNetCore.Http;

namespace Kartova.SharedKernel.AspNetCore;

public sealed class HttpContextCurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _http;
    private readonly ITenantContext _tenantContext;

    public HttpContextCurrentUser(IHttpContextAccessor http, ITenantContext tenantContext)
    {
        _http = http;
        _tenantContext = tenantContext;
    }

    public Guid UserId
    {
        get
        {
            var sub = _http.HttpContext?.User.FindFirstValue("sub")
                      ?? throw new InvalidOperationException("No 'sub' claim on current user.");
            return Guid.Parse(sub);
        }
    }

    public string DisplayName
    {
        get
        {
            var user = _http.HttpContext?.User
                       ?? throw new InvalidOperationException("No HttpContext on current request.");
            return user.FindFirstValue("name")
                   ?? user.FindFirstValue("preferred_username")
                   ?? user.FindFirstValue("email")
                   ?? user.FindFirstValue("sub")
                   ?? throw new InvalidOperationException("No identifying claim on current user.");
        }
    }

    public IReadOnlyList<TeamMembershipInfo> TeamMemberships => _tenantContext.TeamMemberships;

    public IReadOnlySet<Guid> TeamIds => _tenantContext.TeamIds;
}
