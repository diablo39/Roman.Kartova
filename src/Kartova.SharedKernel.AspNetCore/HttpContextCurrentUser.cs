using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Kartova.SharedKernel.AspNetCore;

public sealed class HttpContextCurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _http;

    public HttpContextCurrentUser(IHttpContextAccessor http) => _http = http;

    public Guid UserId
    {
        get
        {
            var sub = _http.HttpContext?.User.FindFirstValue("sub")
                      ?? throw new InvalidOperationException("No 'sub' claim on current user.");
            return Guid.Parse(sub);
        }
    }
}
