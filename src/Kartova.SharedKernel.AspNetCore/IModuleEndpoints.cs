using Microsoft.AspNetCore.Routing;

namespace Kartova.SharedKernel.AspNetCore;

/// <summary>
/// Web-host-side counterpart to <see cref="Kartova.SharedKernel.IModule"/>.
/// Modules that expose HTTP endpoints implement this interface; the migrator
/// (which only runs DDL) does not depend on it.
/// </summary>
public interface IModuleEndpoints
{
    void MapEndpoints(IEndpointRouteBuilder app);
}
