using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Kartova.SharedKernel.AspNetCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
[TestClass]
public class EndpointRouteRules
{
    private const string Get = "GET";
    private const string Post = "POST";
    private const string Put = "PUT";

    // MapHealthChecks matches every verb, so its endpoints carry no HttpMethodMetadata.
    private const string AnyMethod = "";

    /// <summary>
    /// ADR-0118 / TD-015: the only routes outside <c>/api/v1/admin/</c> allowed to carry
    /// <c>PlatformAdminOnly</c> — operator-only ops endpoints that must keep their ADR-0060 URL.
    /// </summary>
    private static readonly string[] OpsAdminRoutes = ["/health/detailed"];

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
        new("EditApplication",             Put,  "/api/v1/catalog/applications/{id:guid}"),
        new("DeprecateApplication",        Post, "/api/v1/catalog/applications/{id:guid}/deprecate"),
        new("DecommissionApplication",     Post, "/api/v1/catalog/applications/{id:guid}/decommission"),

        // Organization (slug doubles as the primary collection per ADR-0092 skip rule)
        new("GetOrganizationMe",           Get,  "/api/v1/organizations/me"),
        new("GetOrganizationMeAdminOnly",  Get,  "/api/v1/organizations/me/admin-only"),

        // Organization admin (BYPASSRLS, separate auth surface, same slug)
        new("AdminCreateOrganization",     Post, "/api/v1/admin/organizations/"),
        new("AdminGetSessionMe",           Get,  "/api/v1/admin/session/me"),

        // Invitation accept — anonymous, tenant-less (slice 9, task 8)
        new("GetInvitationAcceptContext",  Get,  "/api/v1/invitations/accept"),
        new("AcceptInvitation",            Post, "/api/v1/invitations/accept"),

        // System routes (Kartova.Api SystemEndpoints — TD-015)
        new("HealthLive",                  AnyMethod, "/health/live"),
        new("HealthReady",                 AnyMethod, "/health/ready"),
        new("HealthStartup",               AnyMethod, "/health/startup"),
        new("HealthDetailed",              AnyMethod, "/health/detailed"),
        new("GetOpenApiDocument",          Get,  "/openapi/{documentName}.json"),
        new("GetVersion",                  Get,  "/api/v1/version"),
    ];

    [TestMethod]
    public void Every_expected_endpoint_is_registered_with_correct_verb_and_template()
    {
        var actual = MapEndpointsForArchTest();

        foreach (var expected in ExpectedEndpoints)
        {
            var match = actual.SingleOrDefault(e =>
                string.Equals(e.Name, expected.Name, StringComparison.Ordinal));

            Assert.IsNotNull(
                match,
                $"named route '{expected.Name}' must exist — kills `MapGet(...)` → `;` style mutants");
            Assert.AreEqual(
                expected.HttpMethod,
                match!.HttpMethod,
                $"named route '{expected.Name}' must keep its HTTP method");
            Assert.AreEqual(
                expected.Template,
                match.Template,
                $"named route '{expected.Name}' must keep its URL template (ADR-0092)");
        }
    }

    [TestMethod]
    public void Every_module_endpoint_has_a_route_name()
    {
        var actual = MapEndpointsForArchTest();

        var unnamed = actual.Where(e => string.IsNullOrEmpty(e.Name)).ToArray();

        Assert.AreEqual(
            0,
            unnamed.Length,
            "every endpoint mapped via IModuleEndpoints.MapEndpoints must call .WithName(...) " +
            "so the route inventory in EndpointRouteRules can pin verb+template+name. " +
            "Unnamed endpoints (count: " + unnamed.Length + "): " +
            string.Join(", ", unnamed.Select(e => $"{e.HttpMethod} {e.Template}")));
    }

    [TestMethod]
    public void Route_names_are_unique_across_modules()
    {
        var actual = MapEndpointsForArchTest();

        var duplicates = actual
            .Where(e => !string.IsNullOrEmpty(e.Name))
            .GroupBy(e => e.Name)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToArray();

        Assert.AreEqual(
            0,
            duplicates.Length,
            "route names are used as link-relation identifiers and must be unique across the API. " +
            "Duplicates: " + string.Join(", ", duplicates));
    }

    /// <summary>
    /// ADR-0118: every route under /api/v1/admin/ must require the PlatformAdminOnly policy (operator
    /// realm scheme + role). A route mapped there without it would silently fall back to the tenant
    /// scheme — exactly the cross-realm hole the ADR closes.
    /// An endpoint carrying <see cref="Microsoft.AspNetCore.Authorization.IAllowAnonymous"/> is also
    /// an offender even if the policy metadata is present: <c>AuthorizationMiddleware</c> skips
    /// authorization entirely for such endpoints, so <c>MapAdminModule(...).AllowAnonymous()</c> would
    /// keep the PlatformAdminOnly metadata but accept anonymous callers.
    /// </summary>
    [TestMethod]
    public void Every_admin_route_requires_PlatformAdminOnly()
    {
        var offenders = MapEndpointAuthForArchTest()
            .Where(e => e.Template.StartsWith("/api/v1/admin/", StringComparison.OrdinalIgnoreCase))
            .Where(e => !e.Policies.Contains(PlatformAdminAuth.Policy) || e.AllowsAnonymous)
            .Select(e => $"{e.Name} {e.Template}")
            .ToArray();

        Assert.AreEqual(0, offenders.Length,
            "admin routes missing PlatformAdminOnly or marked AllowAnonymous (map via MapAdminModule; " +
            "AllowAnonymous is forbidden on admin routes): " + string.Join(", ", offenders));
    }

    [TestMethod]
    public void PlatformAdminOnly_is_used_only_under_the_admin_prefix()
    {
        var offenders = MapEndpointAuthForArchTest()
            .Where(e => e.Policies.Contains(PlatformAdminAuth.Policy) || e.Schemes.Contains(PlatformAdminAuth.Scheme))
            .Where(e => !e.Template.StartsWith("/api/v1/admin/", StringComparison.OrdinalIgnoreCase))
            .Where(e => !OpsAdminRoutes.Contains(e.Template, StringComparer.OrdinalIgnoreCase))
            .Select(e => $"{e.Name} {e.Template}")
            .ToArray();

        Assert.AreEqual(0, offenders.Length,
            "PlatformAdminOnly (or the PlatformAdmin scheme named directly) outside /api/v1/admin/ mixes operator and tenant surfaces: " +
            string.Join(", ", offenders));
    }

    /// <summary>
    /// TD-015: each allowlisted ops-admin route must exist exactly once and require PlatformAdminOnly
    /// without AllowAnonymous. Replaces the Program.cs comment that pinned /health/detailed by hand.
    /// </summary>
    [TestMethod]
    public void Every_ops_admin_route_requires_PlatformAdminOnly()
    {
        var endpoints = MapEndpointAuthForArchTest();

        foreach (var route in OpsAdminRoutes)
        {
            var matches = endpoints
                .Where(e => string.Equals(e.Template, route, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            Assert.AreEqual(1, matches.Length,
                $"ops-admin route '{route}' must be mapped exactly once — an allowlist entry for a missing route guards nothing.");
            Assert.IsTrue(matches[0].Policies.Contains(PlatformAdminAuth.Policy),
                $"ops-admin route '{route}' must require {PlatformAdminAuth.Policy}.");
            Assert.IsFalse(matches[0].AllowsAnonymous,
                $"ops-admin route '{route}' must not be AllowAnonymous (AuthorizationMiddleware would skip the policy).");
        }
    }

    /// <summary>
    /// TD-015: a route mapped directly in Program.cs is invisible to this sweep (which maps only
    /// IModuleEndpoints). System routes live in SystemEndpoints; this guard keeps it that way.
    /// </summary>
    [TestMethod]
    public void Program_maps_no_routes_directly()
    {
        var source = File.ReadAllText(FindRepoFile("src/Kartova.Api/Program.cs"));

        // Anti-vacuity: prove this is the composition root that grafts SystemEndpoints in.
        Assert.IsTrue(source.Contains("new SystemEndpoints()", StringComparison.Ordinal),
            "Program.cs no longer adds SystemEndpoints — the file layout changed; update this guard.");

        // Matches any `.Map…(` call except `.MapEndpoints(` (the one call this composition root
        // is allowed to make — it grafts each IModuleEndpoints in, it does not map routes itself).
        // A narrower allowlist of verb-specific method names (MapGet/MapPost/…) missed this repo's
        // own route-mapping helpers (MapAdminModule/MapTenantScopedModule in
        // ModuleRouteExtensions.cs) along with plain .Map(...), MapControllers, MapHub, etc.
        var direct = Regex.Matches(source, @"\.Map(?!Endpoints\()\w*\(")
            .Select(m => m.Value)
            .ToArray();

        Assert.AreEqual(0, direct.Length,
            "routes mapped directly in Program.cs are invisible to EndpointRouteRules — map them in SystemEndpoints: " +
            string.Join(", ", direct));
    }

    [TestMethod]
    public void Admin_route_snapshot_is_not_empty()
    {
        // Guards the two rules above against passing vacuously (e.g. discovery found no admin module).
        var adminRoutes = MapEndpointAuthForArchTest()
            .Count(e => e.Template.StartsWith("/api/v1/admin/", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(adminRoutes >= 2, $"expected ≥ 2 admin routes (organizations POST, session/me), found {adminRoutes}.");
    }

    /// <summary>
    /// ADR-0118 gate 7 fix: the two rules above only look at whether the <c>PlatformAdminOnly</c>
    /// policy NAME is attached. ASP.NET Core <em>combines</em> every <see cref="IAuthorizeData"/>
    /// and <see cref="AuthorizationPolicy"/> metadata item on an endpoint — including their
    /// authentication schemes — via <see cref="AuthorizationPolicy.CombineAsync(IAuthorizationPolicyProvider,IEnumerable{IAuthorizeData},IEnumerable{AuthorizationPolicy})"/>.
    /// An admin route additionally carrying e.g. <c>[Authorize(AuthenticationSchemes = "Bearer")]</c>
    /// would keep the policy-name check green while still authenticating tenant-realm tokens. This
    /// test resolves the real combined policy the runtime would use and pins its
    /// <see cref="AuthorizationPolicy.AuthenticationSchemes"/> down to exactly the operator scheme.
    /// </summary>
    [TestMethod]
    public async Task Every_admin_route_combines_to_exactly_the_PlatformAdmin_scheme()
    {
        var app = BuildArchTestApp();
        var provider = app.Services.GetRequiredService<IAuthorizationPolicyProvider>();

        var adminEndpoints = RouteEndpointsOf(app)
            .Where(e => (e.RoutePattern.RawText ?? string.Empty).StartsWith("/api/v1/admin/", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.IsTrue(adminEndpoints.Count >= 2, $"expected ≥ 2 admin routes, found {adminEndpoints.Count}.");

        foreach (var endpoint in adminEndpoints)
        {
            var combined = await AuthorizationPolicy.CombineAsync(
                provider,
                endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(),
                endpoint.Metadata.GetOrderedMetadata<AuthorizationPolicy>());

            Assert.IsNotNull(combined, $"{endpoint.RoutePattern.RawText}: combined authorization policy must not be null");
            CollectionAssert.AreEqual(
                new[] { PlatformAdminAuth.Scheme },
                combined!.AuthenticationSchemes.ToArray(),
                $"{endpoint.RoutePattern.RawText}: combined AuthenticationSchemes must be exactly [{PlatformAdminAuth.Scheme}] — " +
                $"found [{string.Join(", ", combined.AuthenticationSchemes)}]");
        }
    }

    /// <summary>
    /// ADR-0118 (amended 2026-09-28): every admin route binds the <c>KartovaAdminWeb</c> CORS policy, so
    /// only the web-admin origin can read admin responses from a browser. Without it the route would fall
    /// back to the middleware default (tenant origins).
    /// </summary>
    [TestMethod]
    public void Every_admin_route_binds_the_admin_web_cors_policy()
    {
        var adminEndpoints = BuildArchTestEndpoints()
            .Where(e => (e.RoutePattern.RawText ?? string.Empty).StartsWith("/api/v1/admin/", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        // Anti-vacuity: an empty set (broken endpoint discovery) would pass the offender check below.
        CollectionAssert.Contains(adminEndpoints.Select(e => e.RoutePattern.RawText).ToArray(), "/api/v1/admin/session/me",
            "endpoint discovery lost the known admin route — the CORS check below would pass vacuously.");

        var offenders = adminEndpoints
            .Where(e => e.Metadata.GetMetadata<ICorsMetadata>() is not IEnableCorsAttribute { PolicyName: CorsPolicies.AdminWeb })
            .Select(e => e.RoutePattern.RawText)
            .ToArray();

        Assert.AreEqual(0, offenders.Length,
            "admin routes without the KartovaAdminWeb CORS policy (map via MapAdminModule): " + string.Join(", ", offenders));
    }

    [TestMethod]
    public void Admin_web_cors_policy_is_used_only_under_the_admin_prefix()
    {
        var offenders = BuildArchTestEndpoints()
            .Where(e => e.Metadata.GetMetadata<ICorsMetadata>() is IEnableCorsAttribute { PolicyName: CorsPolicies.AdminWeb })
            .Where(e => !(e.RoutePattern.RawText ?? string.Empty).StartsWith("/api/v1/admin/", StringComparison.OrdinalIgnoreCase))
            .Select(e => e.RoutePattern.RawText)
            .ToArray();

        Assert.AreEqual(0, offenders.Length,
            "the KartovaAdminWeb CORS policy outside /api/v1/admin/ lets the operator origin read tenant routes: " +
            string.Join(", ", offenders));
    }

    /// <summary>
    /// Boots a minimal <see cref="WebApplication"/> with just enough services
    /// to make <see cref="IEndpointRouteBuilder"/>-based mapping succeed
    /// (auth/authz are required by <see cref="ModuleRouteExtensions.MapAdminModule"/>),
    /// instantiates every <see cref="IModuleEndpoints"/> implementation found
    /// in production assemblies via parameterless ctor, and calls <c>MapEndpoints</c>.
    /// The real <see cref="PlatformAdminAuth.Policy"/> policy is registered (not just
    /// a placeholder scheme) so <see cref="IAuthorizationPolicyProvider.GetPolicyAsync"/>
    /// resolves it the same way the production host does.
    /// </summary>
    private static WebApplication BuildArchTestApp()
    {
        var builder = WebApplication.CreateBuilder();
        // Auth / authz must be present because MapAdminModule calls RequireAuthorization;
        // we don't need a working JWT pipeline, just a valid scheme registration.
        builder.Services.AddAuthentication("Test").AddJwtBearer("Test", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddAuthorizationBuilder()
            .AddPlatformAdminPolicy();
        builder.Services.AddRouting();
        // Rate limiter must be present because InvitationAcceptRoutes calls RequireRateLimiting.
        builder.Services.AddRateLimiter(_ => { });

        // SystemEndpoints (TD-015): MapHealthChecks builds its middleware pipeline at map time and needs
        // HealthCheckService; MapOpenApi needs the OpenAPI services. No real checks are registered.
        builder.Services.AddHealthChecks();
        builder.Services.AddOpenApi();

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
        // Skip types the host already registers for real (e.g. ILoggerFactory, added by
        // WebApplication.CreateBuilder()'s default logging setup): WebApplicationBuilder.Build()
        // itself resolves ILoggerFactory to construct its internal Logger<T>s, so shadowing it
        // with a null-factory stub — added AFTER the builder's own registration and therefore
        // winning on resolution — throws ArgumentNullException("factory") inside Build(), before
        // any endpoint ever runs. Task 5 / AdminSessionEndpointDelegates.GetMe(ILoggerFactory)
        // surfaced this the first time a discovered delegate parameter collided with a
        // framework-registered service.
        foreach (var type in DiscoverEndpointDelegateServiceTypes())
        {
            if (builder.Services.Any(sd => sd.ServiceType == type)) continue;
            builder.Services.AddTransient(type, _ => null!);
        }

        var app = builder.Build();

        foreach (var moduleType in DiscoverModuleEndpointsTypes())
        {
            var module = (IModuleEndpoints)Activator.CreateInstance(moduleType)!;
            module.MapEndpoints(app);
        }

        return app;
    }

    private static List<RouteEndpoint> RouteEndpointsOf(WebApplication app) =>
        ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(ds => ds.Endpoints)
            .OfType<RouteEndpoint>()
            .ToList();

    private static List<RouteEndpoint> BuildArchTestEndpoints() => RouteEndpointsOf(BuildArchTestApp());

    private static List<EndpointFingerprint> MapEndpointsForArchTest() =>
        BuildArchTestEndpoints()
            .Select(e => new EndpointFingerprint(
                Name: e.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName ?? string.Empty,
                HttpMethod: e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.SingleOrDefault() ?? string.Empty,
                Template: e.RoutePattern.RawText ?? string.Empty))
            .ToList();

    private static List<EndpointAuth> MapEndpointAuthForArchTest() =>
        BuildArchTestEndpoints()
            .Select(e => new EndpointAuth(
                Name: e.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName ?? string.Empty,
                Template: e.RoutePattern.RawText ?? string.Empty,
                Policies: e.Metadata.GetOrderedMetadata<IAuthorizeData>()
                    .Select(a => a.Policy)
                    .Where(p => p is not null)
                    .Select(p => p!)
                    .ToArray(),
                AllowsAnonymous: e.Metadata.GetMetadata<Microsoft.AspNetCore.Authorization.IAllowAnonymous>() is not null,
                Schemes: e.Metadata.GetOrderedMetadata<IAuthorizeData>()
                    .SelectMany(a => (a.AuthenticationSchemes ?? string.Empty)
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    .ToArray()))
            .ToList();

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
                if (!type.Name.EndsWith("EndpointDelegates", StringComparison.Ordinal)
                    && !type.Name.EndsWith("Routes", StringComparison.Ordinal)) continue;
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

    private static string FindRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Kartova.slnx")))
        {
            dir = dir.Parent;
        }
        if (dir is null) throw new InvalidOperationException("Kartova.slnx not found walking up from current directory.");
        return Path.Combine(dir.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private sealed record EndpointFingerprint(string Name, string HttpMethod, string Template);

    private sealed record EndpointAuth(string Name, string Template, string[] Policies, bool AllowsAnonymous, string[] Schemes);
}
