using System.Diagnostics.CodeAnalysis;
using FluentAssertions;
using Kartova.SharedKernel.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Kartova.ArchitectureTests;

/// <summary>
/// Pins ADR-0096 — the Kartova HTTP API does not use the PATCH verb.
/// PUT for full-resource replacement, POST /&lt;action&gt; for named action endpoints.
/// Sparse-update demand is met by named actions, not PATCH semantics.
///
/// Boot mirrors <see cref="EndpointRouteRules"/>: a minimal <see cref="WebApplication"/>
/// is composed with every <see cref="IModuleEndpoints"/> implementation discovered in
/// production assemblies, then the live <see cref="EndpointDataSource"/> is walked and
/// every endpoint's <see cref="HttpMethodMetadata"/> is inspected for the PATCH verb.
/// The arch test is GREEN today (no PATCH route exists); it goes RED if anyone ever
/// adds one — at which point this comment, the ADR, and the offending endpoint must be
/// reconciled before the test can be unsuppressed.
/// </summary>
[ExcludeFromCodeCoverage]
public class RestVerbPolicyRules
{
    [Fact]
    public void No_endpoint_uses_PATCH_verb()
    {
        var endpoints = MapEndpointsForArchTest();

        var patchEndpoints = endpoints
            .Where(e => e.HttpMethods.Contains("PATCH", StringComparer.OrdinalIgnoreCase))
            .Select(e => $"{e.HttpMethods.Single()} {e.Template}")
            .ToList();

        patchEndpoints.Should().BeEmpty(
            because: "ADR-0096 forbids PATCH endpoints. Use PUT for full-resource replacement " +
                     "or POST /<action> for named commands. Offending routes: " +
                     string.Join(", ", patchEndpoints));
    }

    /// <summary>
    /// Boots a minimal <see cref="WebApplication"/> with just enough services to make
    /// <see cref="IEndpointRouteBuilder"/>-based mapping succeed (auth/authz are required
    /// by <c>MapAdminModule</c> et al.), instantiates every <see cref="IModuleEndpoints"/>
    /// implementation in production assemblies via parameterless ctor, calls
    /// <c>MapEndpoints</c>, and snapshots the resulting <see cref="EndpointDataSource"/>
    /// down to (httpMethods, template) pairs. Mirrors
    /// <see cref="EndpointRouteRules.MapEndpointsForArchTest"/>; duplication is preferred
    /// over extraction at two callers.
    /// </summary>
    private static List<EndpointVerbFingerprint> MapEndpointsForArchTest()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddAuthentication("Test").AddJwtBearer("Test", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddRouting();

        // Stub every reference type referenced by an endpoint delegate parameter so
        // RequestDelegateFactory's IServiceProviderIsService check classifies them as
        // [FromServices] rather than [FromBody]. See EndpointRouteRules for the long-form
        // rationale.
        foreach (var type in DiscoverEndpointDelegateServiceTypes())
        {
            builder.Services.AddTransient(type, _ => null!);
        }

        var app = builder.Build();

        foreach (var moduleType in DiscoverModuleEndpointsTypes())
        {
            var module = (IModuleEndpoints)Activator.CreateInstance(moduleType)!;
            module.MapEndpoints(app);
        }

        return ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(ds => ds.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(e => new EndpointVerbFingerprint(
                HttpMethods: e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.ToArray()
                             ?? Array.Empty<string>(),
                Template: e.RoutePattern.RawText ?? string.Empty))
            .ToList();
    }

    private static IEnumerable<Type> DiscoverModuleEndpointsTypes() =>
        AssemblyRegistry.AllProduction()
            .SelectMany(a => a.GetTypes())
            .Where(t => !t.IsAbstract && typeof(IModuleEndpoints).IsAssignableFrom(t))
            .Where(t => t.GetConstructor(Type.EmptyTypes) is not null);

    private static IEnumerable<Type> DiscoverEndpointDelegateServiceTypes()
    {
        var seen = new HashSet<Type>();
        foreach (var assembly in AssemblyRegistry.AllProduction())
        {
            foreach (var type in assembly.GetTypes())
            {
                if (!type.Name.EndsWith("EndpointDelegates", StringComparison.Ordinal)) continue;
                foreach (var method in type.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static))
                {
                    foreach (var p in method.GetParameters())
                    {
                        var pt = p.ParameterType;
                        if (pt.IsValueType) continue;
                        if (pt == typeof(string)) continue;
                        if (pt == typeof(CancellationToken)) continue;
                        if (pt.Namespace?.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal) == true) continue;
                        if (p.GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.FromBodyAttribute), inherit: false).Length > 0) continue;
                        if (seen.Add(pt)) yield return pt;
                    }
                }
            }
        }
    }

    private sealed record EndpointVerbFingerprint(string[] HttpMethods, string Template);
}
