using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Wolverine;

namespace Kartova.SharedKernel;

/// <summary>
/// Every bounded-context module implements this interface. The composition
/// root enumerates all modules and invokes them in order.
/// Enforced by NetArchTest boundary rules (ADR-0082).
/// </summary>
public interface IModule
{
    string Name { get; }
    void RegisterServices(IServiceCollection services, IConfiguration configuration);
    void ConfigureWolverine(WolverineOptions options);
}
