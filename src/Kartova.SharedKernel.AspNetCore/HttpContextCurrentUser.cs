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

    public IReadOnlyList<TeamMembershipInfo> TeamMemberships => _tenantContext.TeamMemberships;

    public IReadOnlySet<Guid> TeamIds => _tenantContext.TeamIds;
}
