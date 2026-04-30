using System.Diagnostics.CodeAnalysis;
using FluentAssertions;
using Kartova.SharedKernel.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Kartova.ArchitectureTests;

/// <summary>
/// Pins the HTTP route surface declared by every <see cref="IModuleEndpoints"/>
/// implementation in the solution. Resolves slice-3 spec §13.9.
///
/// The mutation report's parallel from slice-2 showed `MapGet(...)` mutated to
/// `;` survives — the endpoint disappears and no test catches it. The hard
/// inventory check below kills that mutant for every module's named routes; the
/// soft "every endpoint has a name" guard catches accidental drops on routes
/// that don't yet appear in the inventory.
/// </summary>
[ExcludeFromCodeCoverage]
public class EndpointRouteRules
{
    private const string Get = "GET";
    private const string Post = "POST";

    /// <summary>
    /// The single source of truth for what HTTP routes the API must expose.
    /// Every entry here is asserted against the live <see cref="EndpointDataSource"/>;
    /// missing entries fail the test, extra entries are allowed (covered by the
    /// "every endpoint has a name" guard so they can't drift unnamed).
    /// </summary>
    private static readonly EndpointFingerprint[] ExpectedEndpoints =
    [
        // Catalog (ADR-0092 module-prefixed URL convention)
        new("RegisterApplication",         Post, "/api/v1/catalog/applications"),
        new("GetApplicationById",          Get,  "/api/v1/catalog/applications/{id:guid}"),
        new("ListApplications",            Get,  "/api/v1/catalog/applications"),

        // Organization (slug doubles as the primary collection per ADR-0092 skip rule)
        new("GetOrganizationMe",           Get,  "/api/v1/organizations/me"),
        new("GetOrganizationMeAdminOnly",  Get,  "/api/v1/organizations/me/admin-only"),

        // Organization admin (BYPASSRLS, separate auth surface, same slug)
        new("AdminCreateOrganization",     Post, "/api/v1/admin/organizations/"),
    ];

    [Fact]
    public void Every_expected_endpoint_is_registered_with_correct_verb_and_template()
    {
        var actual = MapEndpointsForArchTest();

        foreach (var expected in ExpectedEndpoints)
        {
            var match = actual.SingleOrDefault(e =>
                string.Equals(e.Name, expected.Name, StringComparison.Ordinal));

            match.Should().NotBeNull(
                because: $"named route '{expected.Name}' must exist — kills `MapGet(...)` → `;` style mutants");
            match!.HttpMethod.Should().Be(expected.HttpMethod,
                because: $"named route '{expected.Name}' must keep its HTTP method");
            match.Template.Should().Be(expected.Template,
                because: $"named route '{expected.Name}' must keep its URL template (ADR-0092)");
        }
    }

    [Fact]
    public void Every_module_endpoint_has_a_route_name()
    {
        var actual = MapEndpointsForArchTest();

        var unnamed = actual.Where(e => string.IsNullOrEmpty(e.Name)).ToArray();

        unnamed.Should().BeEmpty(
            because: "every endpoint mapped via IModuleEndpoints.MapEndpoints must call .WithName(...) " +
                     "so the route inventory in EndpointRouteRules can pin verb+template+name. " +
                     "Unnamed endpoints (count: " + unnamed.Length + "): " +
                     string.Join(", ", unnamed.Select(e => $"{e.HttpMethod} {e.Template}")));
    }

    [Fact]
    public void Route_names_are_unique_across_modules()
    {
        var actual = MapEndpointsForArchTest();

        var duplicates = actual
            .Where(e => !string.IsNullOrEmpty(e.Name))
            .GroupBy(e => e.Name)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToArray();

        duplicates.Should().BeEmpty(
            because: "route names are used as link-relation identifiers and must be unique across the API. " +
                     "Duplicates: " + string.Join(", ", duplicates));
    }

    /// <summary>
    /// Boots a minimal <see cref="WebApplication"/> with just enough services
    /// to make <see cref="IEndpointRouteBuilder"/>-based mapping succeed
    /// (auth/authz are required by <see cref="ModuleRouteExtensions.MapAdminModule"/>),
    /// instantiates every <see cref="IModuleEndpoints"/> implementation found
    /// in production assemblies via parameterless ctor, calls <c>MapEndpoints</c>,
    /// and snapshots the resulting <see cref="EndpointDataSource"/>.
    /// </summary>
    private static List<EndpointFingerprint> MapEndpointsForArchTest()
    {
        var builder = WebApplication.CreateBuilder();
        // Auth / authz must be present because MapAdminModule calls RequireAuthorization;
        // we don't need a working JWT pipeline, just a valid scheme registration.
        builder.Services.AddAuthentication("Test").AddJwtBearer("Test", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddRouting();

        // The endpoint delegates take handler/DbContext/abstraction parameters that
        // RequestDelegateFactory must classify as [FromServices] rather than [FromBody].
        // RDF asks the IServiceProviderIsService whether a type is registered; without
        // a real DI graph we'd have to register every single handler/DbContext just to
        // make the metadata inference pass. Instead, override the marker so any reference
        // type is treated as a service. This only affects the arch-test composition root —
        // production wires types through actual registrations.
        // Register stub services for every reference type referenced by an endpoint
        // delegate parameter. RequestDelegateFactory uses IServiceProviderIsService —
        // backed by ServiceProviderEngine — to decide [FromServices] vs [FromBody].
        // Without registrations the binder treats handler/DbContext parameters as bodies
        // and fails on "multiple bodies". Stubbing them as null factories makes the
        // service-marker positive without requiring real DI graphs.
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
            .Select(e => new EndpointFingerprint(
                Name: e.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName ?? string.Empty,
                HttpMethod: e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.SingleOrDefault() ?? string.Empty,
                Template: e.RoutePattern.RawText ?? string.Empty))
            .ToList();
    }

    private static IEnumerable<Type> DiscoverModuleEndpointsTypes() =>
        AssemblyRegistry.AllProduction()
            .SelectMany(a => a.GetTypes())
            .Where(t => !t.IsAbstract && typeof(IModuleEndpoints).IsAssignableFrom(t))
            .Where(t => t.GetConstructor(Type.EmptyTypes) is not null);

    /// <summary>
    /// Finds every reference-type parameter on every static endpoint delegate
    /// referenced by any <c>*EndpointDelegates</c> class in production assemblies.
    /// Registering each as a null-factory transient makes
    /// <see cref="IServiceProviderIsService.IsService"/> return true so RDF binds
    /// them as <c>[FromServices]</c> rather than failing inference.
    /// </summary>
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
                        // Skip [FromBody] request types — they should NOT be treated as services.
                        if (p.GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.FromBodyAttribute), inherit: false).Length > 0) continue;
                        if (seen.Add(pt)) yield return pt;
                    }
                }
            }
        }
    }

    private sealed record EndpointFingerprint(string Name, string HttpMethod, string Template);
}
