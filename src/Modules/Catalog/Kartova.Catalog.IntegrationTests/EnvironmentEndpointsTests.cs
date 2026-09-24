using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.Catalog.Infrastructure;   // CatalogDbContext, CatalogEndpointDelegates, RegisterEnvironmentHandler
using Kartova.SharedKernel.AspNetCore;   // ICurrentUser, ProblemTypes
using Kartova.SharedKernel.Audit;   // IAuditWriter, AuditEntry
using Kartova.SharedKernel.Multitenancy;
using Kartova.SharedKernel.Pagination;
using Kartova.Testing.Auth;
using Microsoft.AspNetCore.Http;   // StatusCodes
using Microsoft.AspNetCore.Http.HttpResults;   // ProblemHttpResult
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;   // ChangeTracker
using Microsoft.EntityFrameworkCore.Diagnostics;   // SaveChangesInterceptor
using Microsoft.Extensions.Logging.Abstractions;   // NullLogger
using Npgsql;

namespace Kartova.Catalog.IntegrationTests;

/// <summary>
/// Real-seam (gate-3) integration tests for the Environment slice-1 endpoints (E-02.F-05.S-01):
/// <c>POST /catalog/environments</c>, <c>GET /catalog/environments</c>,
/// <c>GET /catalog/environments/{id}</c>. Exercises the real Postgres/RLS + JWT seam via
/// <see cref="KartovaApiFixtureBase"/> — proves the register/list/get wiring, the per-tenant
/// display-name uniqueness (pre-check + DB backstop), resource-details validation, the type/region
/// list filters, and tenant isolation (Environment has no owning team — see
/// <see cref="InfrastructureVmEndpointsTests"/> for the equivalent team-owned slice).
/// </summary>
[TestClass]
public sealed class EnvironmentEndpointsTests : CatalogIntegrationTestBase
{
    // ADR-0109: enums travel on the wire as camelCase strings.
    private static object Body(
        string displayName, EnvironmentType type = EnvironmentType.Production,
        string description = "desc", string? region = "eu-west-1")
        => new
        {
            displayName,
            description,
            type = type.ToString().ToLowerInvariant(),
            region,
            resourceDetails = new Dictionary<string, string>(),
        };

    [TestMethod]
    public async Task RegisterEnvironment_returns_201_and_roundtrips()
    {
        var client = await Fx.CreateAuthenticatedClientAsync("admin@env-a.test");
        var post = await client.PostAsJsonAsync("/api/v1/catalog/environments", Body("Prod EU"));
        Assert.AreEqual(HttpStatusCode.Created, post.StatusCode);

        var created = await post.Content.ReadFromJsonAsync<EnvironmentDetailResponse>(KartovaApiFixtureBase.WireJson);
        Assert.IsNotNull(created);
        Assert.AreEqual("Prod EU", created!.DisplayName);
        Assert.AreEqual(EnvironmentType.Production, created.Type);

        var get = await client.GetAsync($"/api/v1/catalog/environments/{created.Id}");
        Assert.AreEqual(HttpStatusCode.OK, get.StatusCode);
        var fetched = await get.Content.ReadFromJsonAsync<EnvironmentDetailResponse>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(created.Id, fetched!.Id);
        Assert.AreEqual("eu-west-1", fetched.Region);
    }

    [TestMethod]
    public async Task RegisterEnvironment_without_permission_returns_403()
    {
        var viewer = await Fx.CreateAuthenticatedClientAsync("viewer@env-a.test", roles: new[] { KartovaRoles.Viewer });
        var post = await viewer.PostAsJsonAsync("/api/v1/catalog/environments", Body("No Perm"));
        Assert.AreEqual(HttpStatusCode.Forbidden, post.StatusCode);
    }

    [TestMethod]
    public async Task RegisterEnvironment_duplicate_name_returns_409_with_type()
    {
        var client = await Fx.CreateAuthenticatedClientAsync("admin@env-dup.test");
        Assert.AreEqual(HttpStatusCode.Created,
            (await client.PostAsJsonAsync("/api/v1/catalog/environments", Body("Staging"))).StatusCode);

        var dup = await client.PostAsJsonAsync("/api/v1/catalog/environments", Body("Staging"));
        Assert.AreEqual(HttpStatusCode.Conflict, dup.StatusCode);
        var body = await dup.Content.ReadAsStringAsync();
        StringAssert.Contains(body, "environment-name-conflict");
    }

    [TestMethod]
    public async Task RegisterEnvironment_invalid_resource_details_returns_400()
    {
        var client = await Fx.CreateAuthenticatedClientAsync("admin@env-a.test");
        var bad = new
        {
            displayName = "Bad",
            description = "d",
            type = "development",
            region = (string?)null,
            resourceDetails = new Dictionary<string, string> { [new string('k', 200)] = "v" },
        };
        var post = await client.PostAsJsonAsync("/api/v1/catalog/environments", bad);
        Assert.AreEqual(HttpStatusCode.BadRequest, post.StatusCode);
    }

    [TestMethod]
    public async Task ListEnvironments_default_sort_is_display_name_asc()
    {
        var client = await Fx.CreateAuthenticatedClientAsync("admin@env-list.test");
        foreach (var n in new[] { "Charlie", "Alpha", "Bravo" })
            await client.PostAsJsonAsync("/api/v1/catalog/environments", Body(n));

        var page = await client.GetFromJsonAsync<CursorPage<EnvironmentListItemResponse>>(
            "/api/v1/catalog/environments", KartovaApiFixtureBase.WireJson);
        var names = page!.Items.Select(i => i.DisplayName).ToList();
        CollectionAssert.AreEqual(new[] { "Alpha", "Bravo", "Charlie" }, names);
    }

    [TestMethod]
    public async Task ListEnvironments_filters_by_type_and_region()
    {
        var client = await Fx.CreateAuthenticatedClientAsync("admin@env-filter.test");
        await client.PostAsJsonAsync("/api/v1/catalog/environments", Body("Prod A", EnvironmentType.Production, region: "eu"));
        await client.PostAsJsonAsync("/api/v1/catalog/environments", Body("Dev A", EnvironmentType.Development, region: "us"));

        var byType = await client.GetFromJsonAsync<CursorPage<EnvironmentListItemResponse>>(
            "/api/v1/catalog/environments?type=production", KartovaApiFixtureBase.WireJson);
        Assert.IsTrue(byType!.Items.All(i => i.Type == EnvironmentType.Production));

        var byRegion = await client.GetFromJsonAsync<CursorPage<EnvironmentListItemResponse>>(
            "/api/v1/catalog/environments?region=us", KartovaApiFixtureBase.WireJson);
        Assert.IsTrue(byRegion!.Items.All(i => i.Region == "us"));
    }

    [TestMethod]
    public async Task ListEnvironments_rejects_invalid_type_filter_400()
    {
        var client = await Fx.CreateAuthenticatedClientAsync("admin@env-a.test");
        var resp = await client.GetAsync("/api/v1/catalog/environments?type=bogus");
        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [TestMethod]
    public async Task GetEnvironment_unknown_id_returns_404()
    {
        var client = await Fx.CreateAuthenticatedClientAsync("admin@env-a.test");
        var resp = await client.GetAsync($"/api/v1/catalog/environments/{Guid.NewGuid()}");
        Assert.AreEqual(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [TestMethod]
    public async Task Environment_is_tenant_isolated()
    {
        var a = await Fx.CreateAuthenticatedClientAsync("admin@tenant-one.test");
        var post = await a.PostAsJsonAsync("/api/v1/catalog/environments", Body("Isolated"));
        var created = await post.Content.ReadFromJsonAsync<EnvironmentDetailResponse>(KartovaApiFixtureBase.WireJson);

        var b = await Fx.CreateAuthenticatedClientAsync("admin@tenant-two.test");
        var crossGet = await b.GetAsync($"/api/v1/catalog/environments/{created!.Id}");
        Assert.AreEqual(HttpStatusCode.NotFound, crossGet.StatusCode);

        // Same display name in a different tenant must be allowed (uniqueness is per-tenant).
        var bPost = await b.PostAsJsonAsync("/api/v1/catalog/environments", Body("Isolated"));
        Assert.AreEqual(HttpStatusCode.Created, bPost.StatusCode);
    }

    /// <summary>
    /// Backend-review fix A: <c>displayNameContains</c> ILIKE filter must escape the two
    /// ILIKE wildcard metacharacters (<see cref="LikeEscaping"/>) so a literal <c>%</c>/<c>_</c>
    /// in the query only matches rows that actually contain that character, rather than the
    /// unescaped wildcard meaning ("any run of characters" / "any single character") that would
    /// match every row.
    /// </summary>
    [TestMethod]
    public async Task ListEnvironments_displayNameContains_escapes_percent_and_underscore_literally()
    {
        var client = await Fx.CreateAuthenticatedClientAsync("admin@env-like.test");
        await client.PostAsJsonAsync("/api/v1/catalog/environments", Body("Foo%Bar"));
        await client.PostAsJsonAsync("/api/v1/catalog/environments", Body("Foo_Baz"));
        await client.PostAsJsonAsync("/api/v1/catalog/environments", Body("FooQux"));
        await client.PostAsJsonAsync("/api/v1/catalog/environments", Body("Other"));

        // A literal '%' must match only the row that actually contains it — an unescaped ILIKE
        // pattern ('%%%') would instead match every row.
        var percentPage = await client.GetFromJsonAsync<CursorPage<EnvironmentListItemResponse>>(
            $"/api/v1/catalog/environments?displayNameContains={Uri.EscapeDataString("%")}", KartovaApiFixtureBase.WireJson);
        CollectionAssert.AreEqual(new[] { "Foo%Bar" }, percentPage!.Items.Select(i => i.DisplayName).ToList());

        // A literal '_' must match only the row that actually contains it — an unescaped ILIKE
        // pattern ('%_%') would instead match every row, since '_' means "any single character".
        var underscorePage = await client.GetFromJsonAsync<CursorPage<EnvironmentListItemResponse>>(
            $"/api/v1/catalog/environments?displayNameContains={Uri.EscapeDataString("_")}", KartovaApiFixtureBase.WireJson);
        CollectionAssert.AreEqual(new[] { "Foo_Baz" }, underscorePage!.Items.Select(i => i.DisplayName).ToList());

        // Sanity: an ordinary substring still matches case-insensitively as usual.
        var qPage = await client.GetFromJsonAsync<CursorPage<EnvironmentListItemResponse>>(
            "/api/v1/catalog/environments?displayNameContains=qux", KartovaApiFixtureBase.WireJson);
        CollectionAssert.AreEqual(new[] { "FooQux" }, qPage!.Items.Select(i => i.DisplayName).ToList());
    }

    /// <summary>
    /// Backend-review fix A: exercises the load-bearing <c>EnvironmentSortSpecs.Region
    /// { IsNullable = true }</c> path (TD-001) — NULLs sort LAST under ascending order.
    /// </summary>
    [TestMethod]
    public async Task ListEnvironments_sortBy_region_nulls_last_ascending()
    {
        var client = await Fx.CreateAuthenticatedClientAsync("admin@env-region-sort-asc.test");
        await client.PostAsJsonAsync("/api/v1/catalog/environments", Body("B Region", region: "b-region"));
        await client.PostAsJsonAsync("/api/v1/catalog/environments", Body("A Region", region: "a-region"));
        await client.PostAsJsonAsync("/api/v1/catalog/environments", Body("Null One", region: null));
        await client.PostAsJsonAsync("/api/v1/catalog/environments", Body("Null Two", region: null));

        var page = await client.GetFromJsonAsync<CursorPage<EnvironmentListItemResponse>>(
            "/api/v1/catalog/environments?sortBy=region&sortOrder=asc&limit=10", KartovaApiFixtureBase.WireJson);
        var regions = page!.Items.Select(i => i.Region).ToList();

        Assert.AreEqual(4, regions.Count);
        CollectionAssert.AreEqual(
            new[] { "a-region", "b-region" }, regions.Take(2).ToList(), "non-null regions sort first, ascending");
        Assert.IsTrue(regions.Skip(2).All(r => r is null), $"null regions must sort last (asc); got: {string.Join(", ", regions)}");
    }

    /// <summary>DESC counterpart: NULLs sort FIRST under descending order (TD-001).</summary>
    [TestMethod]
    public async Task ListEnvironments_sortBy_region_nulls_first_descending()
    {
        var client = await Fx.CreateAuthenticatedClientAsync("admin@env-region-sort-desc.test");
        await client.PostAsJsonAsync("/api/v1/catalog/environments", Body("B Region", region: "b-region"));
        await client.PostAsJsonAsync("/api/v1/catalog/environments", Body("A Region", region: "a-region"));
        await client.PostAsJsonAsync("/api/v1/catalog/environments", Body("Null One", region: null));
        await client.PostAsJsonAsync("/api/v1/catalog/environments", Body("Null Two", region: null));

        var page = await client.GetFromJsonAsync<CursorPage<EnvironmentListItemResponse>>(
            "/api/v1/catalog/environments?sortBy=region&sortOrder=desc&limit=10", KartovaApiFixtureBase.WireJson);
        var regions = page!.Items.Select(i => i.Region).ToList();

        Assert.AreEqual(4, regions.Count);
        Assert.IsTrue(regions.Take(2).All(r => r is null), $"null regions must sort first (desc); got: {string.Join(", ", regions)}");
        CollectionAssert.AreEqual(
            new[] { "b-region", "a-region" }, regions.Skip(2).ToList(), "non-null regions sort last, descending");
    }

    [TestMethod]
    public async Task ListEnvironments_rejects_unknown_sortBy_with_400()
    {
        var client = await Fx.CreateAuthenticatedClientAsync("admin@env-a.test");
        var resp = await client.GetAsync("/api/v1/catalog/environments?sortBy=bogus");
        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    /// <summary>
    /// Backend-review fix A: registers more than <c>limit</c> environments matching a
    /// <c>type</c> filter plus one non-matching "noise" row, then pages forward via
    /// <c>nextCursor</c> (<c>PrevCursor</c> is reserved on the wire but always null in MVP —
    /// see <see cref="CursorPage{T}"/> — so there is no backward cursor to exercise). Asserts
    /// every filtered row is returned exactly once, in stable displayName-ascending order,
    /// with the noise row never appearing on any page.
    /// </summary>
    [TestMethod]
    public async Task ListEnvironments_filtered_cursor_pages_forward_without_dupes_or_drops()
    {
        var client = await Fx.CreateAuthenticatedClientAsync("admin@env-cursor.test");
        var expectedNames = new List<string>();
        for (var i = 0; i < 7; i++)
        {
            var name = $"Dev {i:D2}";
            expectedNames.Add(name);
            await client.PostAsJsonAsync("/api/v1/catalog/environments", Body(name, EnvironmentType.Development));
        }
        // Noise outside the filter — must never appear in the filtered pages.
        await client.PostAsJsonAsync("/api/v1/catalog/environments", Body("Prod Noise", EnvironmentType.Production));

        var allNames = new List<string>();
        string? cursor = null;
        for (var page = 0; page < 10; page++)
        {
            var url = "/api/v1/catalog/environments?type=development&limit=3"
                + (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}");
            var resp = await client.GetAsync(url);
            Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode, $"page {page} failed: {await resp.Content.ReadAsStringAsync()}");
            var thisPage = await resp.Content.ReadFromJsonAsync<CursorPage<EnvironmentListItemResponse>>(KartovaApiFixtureBase.WireJson);
            Assert.IsTrue(
                thisPage!.Items.All(i => i.Type == EnvironmentType.Development),
                "the noise row (a different type) must never appear on a filtered page");
            allNames.AddRange(thisPage.Items.Select(i => i.DisplayName));
            cursor = thisPage.NextCursor;
            if (cursor is null) break;
        }

        Assert.AreEqual(expectedNames.Count, allNames.Count, "no rows dropped across the filtered cursor pages");
        Assert.AreEqual(allNames.Count, allNames.Distinct().Count(), "no duplicate rows across the filtered cursor pages");
        CollectionAssert.AreEqual(
            expectedNames.OrderBy(n => n, StringComparer.Ordinal).ToList(), allNames,
            "stable displayName-ascending ordering across pages");
    }

    /// <summary>
    /// Backend-review fix A: deterministic (no threads, no Task.WhenAll) reproduction of the
    /// 23505-race backstop in <see cref="CatalogEndpointDelegates.RegisterEnvironmentAsync"/> —
    /// mirrors the <c>RaceOnSaveInterceptor</c> insert-race pattern in
    /// <c>SetComponentSystemTests</c> (e.g. <c>PUT_when_a_concurrent_writer_wins_for_a_DIFFERENT_system_still_returns_409</c>).
    /// This request's own pre-check (<c>AnyAsync</c>) finds no conflict, but a concurrent writer's
    /// raw-SQL insert lands — via the interceptor — between that pre-check and this handler's own
    /// <c>SaveChangesAsync</c>, reproducing the 23505 unique-violation on
    /// <c>ux_catalog_environments_tenant_id_display_name</c> the DB-level backstop exists to catch.
    /// Proves the endpoint returns 409 <c>environment-name-conflict</c>, not 500: see the catch's
    /// XML doc comment in <see cref="CatalogEndpointDelegates.RegisterEnvironmentAsync"/> for why
    /// EF Core's automatic savepoint keeps <c>TenantScopeCommitEndpointFilter</c>'s unconditional
    /// post-catch <c>CommitAsync</c> from turning this into a 500. Calls the delegate directly
    /// (not through HTTP) with fakes for <c>ITenantContext</c>/<c>ICurrentUser</c>/<c>IAuditWriter</c>
    /// — the HTTP status mapping isn't what this race exercises, the delegate's own exception
    /// handling is.
    /// </summary>
    [TestMethod]
    public async Task RegisterEnvironment_race_backstop_returns_409_not_500()
    {
        var tenant = Fx.TenantIdForEmail("admin@env-race.test");
        await Fx.CreateAuthenticatedClientAsync("admin@env-race.test"); // provisions the tenant/org
        const string name = "Race Env";

        var raced = false;
        var interceptor = new RaceOnSaveInterceptor(
            tracker => tracker.Entries<CatalogEnvironment>().Any(e => e.State == EntityState.Added),
            async () =>
            {
                raced = true;
                await using var conn = new NpgsqlConnection(Fx.BypassConnectionString);
                await conn.OpenAsync();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    INSERT INTO catalog_environments
                        (id, tenant_id, display_name, description, type, resource_details, created_by_user_id, created_at)
                    VALUES ($1, $2, $3, 'race-winner', 0, '{}', $4, NOW())
                    """;
                cmd.Parameters.AddWithValue(Guid.NewGuid());
                cmd.Parameters.AddWithValue(tenant.Value);
                cmd.Parameters.AddWithValue(name);
                cmd.Parameters.AddWithValue(Guid.NewGuid());
                await cmd.ExecuteNonQueryAsync();
            });
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql(Fx.BypassConnectionString)
            .AddInterceptors(interceptor)
            .Options;
        await using var db = new CatalogDbContext(options);

        var result = await CatalogEndpointDelegates.RegisterEnvironmentAsync(
            new RegisterEnvironmentRequest(name, "desc", EnvironmentType.Production, null, new Dictionary<string, string>()),
            new RegisterEnvironmentHandler(TimeProvider.System),
            db, new FakeTenantContext(tenant), new FakeCurrentUser(), new NoOpAuditWriter(),
            NullLogger<RegisterEnvironmentHandler>.Instance, default);

        Assert.IsTrue(raced, "the interceptor must actually have raced the insert for this test to be meaningful");
        var problem = result as ProblemHttpResult;
        Assert.IsNotNull(problem, $"expected a ProblemHttpResult (409), got {result.GetType().Name}");
        Assert.AreEqual(StatusCodes.Status409Conflict, problem!.StatusCode);
        Assert.AreEqual(ProblemTypes.EnvironmentNameConflict, problem.ProblemDetails.Type);
    }

    // ---- A2: PUT /environments/{id} + DELETE /environments/{id} with If-Match --------

    private static EditEnvironmentRequest EditFrom(EnvironmentDetailResponse created, string? displayName = null) => new(
        DisplayName: displayName ?? created.DisplayName,
        Description: created.Description,
        Region: created.Region,
        ResourceDetails: created.ResourceDetails);

    private static HttpRequestMessage NewPut(Guid id, string? ifMatch, EditEnvironmentRequest request)
    {
        var msg = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/catalog/environments/{id}")
        {
            Content = JsonContent.Create(request, options: KartovaApiFixtureBase.WireJson),
        };
        if (ifMatch is not null) msg.Headers.TryAddWithoutValidation("If-Match", $"\"{ifMatch}\"");
        return msg;
    }

    private static HttpRequestMessage NewDelete(Guid id, string? ifMatch)
    {
        var msg = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/catalog/environments/{id}");
        if (ifMatch is not null) msg.Headers.TryAddWithoutValidation("If-Match", $"\"{ifMatch}\"");
        return msg;
    }

    private static async Task<EnvironmentDetailResponse> RegisterEnvAsync(HttpClient client, string displayName)
    {
        var post = await client.PostAsJsonAsync("/api/v1/catalog/environments", Body(displayName));
        Assert.AreEqual(HttpStatusCode.Created, post.StatusCode, $"RegisterEnvironment failed: {await post.Content.ReadAsStringAsync()}");
        return (await post.Content.ReadFromJsonAsync<EnvironmentDetailResponse>(KartovaApiFixtureBase.WireJson))!;
    }

    [TestMethod]
    public async Task Put_UpdatesEnvironment_Returns200()
    {
        var client = await Fx.CreateAuthenticatedClientAsync("admin@env-put.test");
        var created = await RegisterEnvAsync(client, "Put Env");

        var resp = await client.SendAsync(NewPut(created.Id, created.Version, EditFrom(created, displayName: "Put Env Renamed")));

        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode, $"EditEnvironment failed: {await resp.Content.ReadAsStringAsync()}");
        var body = await resp.Content.ReadFromJsonAsync<EnvironmentDetailResponse>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual("Put Env Renamed", body!.DisplayName);
        Assert.AreNotEqual(created.Version, body.Version);
        Assert.AreEqual($"\"{body.Version}\"", resp.Headers.ETag?.Tag);
    }

    /// <summary>Exercises the actual HTTP/EF write path for the non-displayName mutable
    /// fields — every other PUT test in this file only varies <c>displayName</c> via
    /// <see cref="EditFrom"/>, so a field-mapping slip in <see cref="EditEnvironmentCommand"/>
    /// construction or <see cref="EditEnvironmentHandler.Handle"/> would only be caught by the
    /// domain-level <c>Edit_replaces_mutable_fields</c> unit test, not by anything hitting the
    /// endpoint.</summary>
    [TestMethod]
    public async Task Put_UpdatesDescriptionRegionAndResourceDetails_Returns200()
    {
        var client = await Fx.CreateAuthenticatedClientAsync("admin@env-put-fields.test");
        var created = await RegisterEnvAsync(client, "Put Fields Env");

        var request = new EditEnvironmentRequest(
            DisplayName: created.DisplayName,
            Description: "Updated description",
            Region: "ap-southeast-1",
            ResourceDetails: new Dictionary<string, string> { ["k"] = "v" });
        var resp = await client.SendAsync(NewPut(created.Id, created.Version, request));

        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode, $"EditEnvironment failed: {await resp.Content.ReadAsStringAsync()}");
        var body = await resp.Content.ReadFromJsonAsync<EnvironmentDetailResponse>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual("Updated description", body!.Description);
        Assert.AreEqual("ap-southeast-1", body.Region);
        Assert.AreEqual("v", body.ResourceDetails["k"]);

        var get = await client.GetFromJsonAsync<EnvironmentDetailResponse>(
            $"/api/v1/catalog/environments/{created.Id}", KartovaApiFixtureBase.WireJson);
        Assert.AreEqual("Updated description", get!.Description);
        Assert.AreEqual("ap-southeast-1", get.Region);
        Assert.AreEqual("v", get.ResourceDetails["k"]);
    }

    /// <summary>Mirrors <c>RegisterEnvironment_invalid_resource_details_returns_400</c> — proves
    /// the same <c>EnvironmentResourceDetails.Validate</c> wiring on the edit path.</summary>
    [TestMethod]
    public async Task Put_InvalidResourceDetails_Returns400()
    {
        var client = await Fx.CreateAuthenticatedClientAsync("admin@env-put-bad-resource.test");
        var created = await RegisterEnvAsync(client, "Put Bad Resource Env");

        var request = new EditEnvironmentRequest(
            DisplayName: created.DisplayName,
            Description: created.Description,
            Region: created.Region,
            ResourceDetails: new Dictionary<string, string> { [new string('k', 200)] = "v" });
        var resp = await client.SendAsync(NewPut(created.Id, created.Version, request));

        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [TestMethod]
    public async Task Put_RenameToExistingName_Returns409()
    {
        var client = await Fx.CreateAuthenticatedClientAsync("admin@env-put-conflict.test");
        var created = await RegisterEnvAsync(client, "Put Conflict A");
        await RegisterEnvAsync(client, "Put Conflict B");

        var resp = await client.SendAsync(NewPut(created.Id, created.Version, EditFrom(created, displayName: "Put Conflict B")));

        Assert.AreEqual(HttpStatusCode.Conflict, resp.StatusCode);
        var problem = await resp.Content.ReadFromJsonAsync<ProblemPayload>();
        Assert.AreEqual(ProblemTypes.EnvironmentNameConflict, problem!.Type);
    }

    /// <summary>Design §"Domain": <c>Edit</c> — "<c>Type</c> immutable". A PUT body with a
    /// different <c>type</c> is accepted (the request no longer carries a `type` field to
    /// change), and the stored type is unaffected.</summary>
    [TestMethod]
    public async Task Put_TypeStaysUnchanged()
    {
        var client = await Fx.CreateAuthenticatedClientAsync("admin@env-put-type-immutable.test");
        var created = await RegisterEnvAsync(client, "Type Immutable Env");
        Assert.AreEqual(EnvironmentType.Production, created.Type);

        // `EditEnvironmentRequest` has no `type` field, so a caller cannot express "change
        // the type" through the typed contract — but a raw JSON body could still smuggle one
        // in if System.Text.Json's default unmapped-member tolerance ever changed. Sending it
        // over the wire directly (rather than through EditFrom, whose typed request has no
        // slot for it) is what actually proves the server ignores it rather than merely proving
        // the C# client can't construct it.
        var raw = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/catalog/environments/{created.Id}")
        {
            Content = JsonContent.Create(new
            {
                displayName = "Type Immutable Env Renamed",
                description = created.Description,
                region = created.Region,
                resourceDetails = created.ResourceDetails,
                type = "development",
            }),
        };
        raw.Headers.TryAddWithoutValidation("If-Match", $"\"{created.Version}\"");

        var resp = await client.SendAsync(raw);

        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode, $"EditEnvironment failed: {await resp.Content.ReadAsStringAsync()}");
        var body = await resp.Content.ReadFromJsonAsync<EnvironmentDetailResponse>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(EnvironmentType.Production, body!.Type, "an extraneous 'type' field on the wire must be ignored, not applied");

        var get = await client.GetFromJsonAsync<EnvironmentDetailResponse>(
            $"/api/v1/catalog/environments/{created.Id}", KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(EnvironmentType.Production, get!.Type, "the persisted type must be unchanged too, not just the PUT response");
    }

    /// <summary>Deterministic (no threads) reproduction of the 23505-race backstop on the
    /// UPDATE path — mirrors <see cref="RegisterEnvironment_race_backstop_returns_409_not_500"/>
    /// but exercises <see cref="CatalogEndpointDelegates.EditEnvironmentAsync"/>'s shared
    /// <c>IsEnvironmentNameConflictRace</c> catch instead of Register's. A concurrent writer's
    /// raw-SQL rename lands between this request's own pre-check and its SaveChangesAsync.</summary>
    [TestMethod]
    public async Task EditEnvironment_race_backstop_returns_409_not_500()
    {
        var tenant = Fx.TenantIdForEmail("admin@env-edit-race.test");
        var client = await Fx.CreateAuthenticatedClientAsync("admin@env-edit-race.test");
        var created = await RegisterEnvAsync(client, "Edit Race Env");
        const string racingName = "Edit Race Winner";

        var raced = false;
        var interceptor = new RaceOnSaveInterceptor(
            tracker => tracker.Entries<CatalogEnvironment>().Any(e => e.State == EntityState.Modified),
            async () =>
            {
                raced = true;
                await using var conn = new NpgsqlConnection(Fx.BypassConnectionString);
                await conn.OpenAsync();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    INSERT INTO catalog_environments
                        (id, tenant_id, display_name, description, type, resource_details, created_by_user_id, created_at)
                    VALUES ($1, $2, $3, 'race-winner', 0, '{}', $4, NOW())
                    """;
                cmd.Parameters.AddWithValue(Guid.NewGuid());
                cmd.Parameters.AddWithValue(tenant.Value);
                cmd.Parameters.AddWithValue(racingName);
                cmd.Parameters.AddWithValue(Guid.NewGuid());
                await cmd.ExecuteNonQueryAsync();
            });
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql(Fx.BypassConnectionString)
            .AddInterceptors(interceptor)
            .Options;
        await using var db = new CatalogDbContext(options);

        var result = await CatalogEndpointDelegates.EditEnvironmentAsync(
            created.Id,
            new EditEnvironmentRequest(racingName, "renamed", created.Region, created.ResourceDetails),
            new EditEnvironmentHandler(),
            db,
            FakeHttpContextWithIfMatch(created.Version),
            new NoOpAuditWriter(),
            NullLogger<EditEnvironmentHandler>.Instance, default);

        Assert.IsTrue(raced, "the interceptor must actually have raced the rename for this test to be meaningful");
        var problem = result as ProblemHttpResult;
        Assert.IsNotNull(problem, $"expected a ProblemHttpResult (409), got {result.GetType().Name}");
        Assert.AreEqual(StatusCodes.Status409Conflict, problem!.StatusCode);
        Assert.AreEqual(ProblemTypes.EnvironmentNameConflict, problem.ProblemDetails.Type);
    }

    private static HttpContext FakeHttpContextWithIfMatch(string version)
    {
        Assert.IsTrue(VersionEncoding.TryDecode(version, out var expected), "test setup: version must decode");
        var http = new DefaultHttpContext();
        http.Items[IfMatchEndpointFilter.ExpectedVersionKey] = expected;
        return http;
    }

    [TestMethod]
    public async Task Put_UnchangedName_Returns200()
    {
        // Renaming to its own current value must not trip the conflict pre-check
        // (the delegate skips it when the name is unchanged).
        var client = await Fx.CreateAuthenticatedClientAsync("admin@env-put-samename.test");
        var created = await RegisterEnvAsync(client, "Same Name Env");

        var resp = await client.SendAsync(NewPut(created.Id, created.Version, EditFrom(created)));

        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
    }

    [TestMethod]
    public async Task Put_StaleIfMatch_Returns412()
    {
        var client = await Fx.CreateAuthenticatedClientAsync("admin@env-put-stale.test");
        var created = await RegisterEnvAsync(client, "Stale Env");

        var ok = await client.SendAsync(NewPut(created.Id, created.Version, EditFrom(created, displayName: "Stale Env v2")));
        Assert.AreEqual(HttpStatusCode.OK, ok.StatusCode);
        var okBody = await ok.Content.ReadFromJsonAsync<EnvironmentDetailResponse>(KartovaApiFixtureBase.WireJson);

        var stale = await client.SendAsync(NewPut(created.Id, created.Version, EditFrom(created, displayName: "Stale Env v3")));
        Assert.AreEqual(HttpStatusCode.PreconditionFailed, stale.StatusCode);

        var problem = await stale.Content.ReadFromJsonAsync<ProblemPayload>();
        Assert.AreEqual(okBody!.Version, problem!.CurrentVersion);
    }

    [TestMethod]
    public async Task Put_MissingIfMatch_Returns428()
    {
        var client = await Fx.CreateAuthenticatedClientAsync("admin@env-put-noifmatch.test");
        var created = await RegisterEnvAsync(client, "NoIfMatch Env");

        var resp = await client.SendAsync(NewPut(created.Id, ifMatch: null, EditFrom(created, displayName: "NoIfMatch Env v2")));

        Assert.AreEqual(HttpStatusCode.PreconditionRequired, resp.StatusCode);
    }

    [TestMethod]
    public async Task Put_WithoutEditPerm_Returns403()
    {
        var client = await Fx.CreateAuthenticatedClientAsync("admin@env-put-noperm.test");
        var created = await RegisterEnvAsync(client, "NoPerm Env");

        var viewer = await Fx.CreateAuthenticatedClientAsync(
            "viewer-env-edit@env-put-noperm.test", new[] { KartovaRoles.Viewer });

        var resp = await viewer.SendAsync(NewPut(created.Id, created.Version, EditFrom(created, displayName: "Hijacked")));

        Assert.AreEqual(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    /// <summary>Proves the edit gate is <c>CatalogEnvironmentsEdit</c>, granted to Member (not
    /// just OrgAdmin) — a Member has no register-perm dependency here since Edit is its own
    /// dedicated permission (design §"Permissions").</summary>
    [TestMethod]
    public async Task Put_AsMemberWithEditPerm_Returns200()
    {
        var admin = await Fx.CreateAuthenticatedClientAsync("admin@env-put-member.test");
        var created = await RegisterEnvAsync(admin, "Member Edit Env");

        var member = await Fx.CreateAuthenticatedClientAsync(
            "member-env-edit@env-put-member.test", new[] { KartovaRoles.Member });

        var resp = await member.SendAsync(NewPut(created.Id, created.Version, EditFrom(created, displayName: "Member Edit Env v2")));

        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode, $"EditEnvironment failed: {await resp.Content.ReadAsStringAsync()}");
    }

    /// <summary>gate-7 C1 equivalent: a blank display name on PUT must surface the global 400
    /// ValidationFailed mapping (mirrors <c>Put_BadAttributes_Returns400</c> in
    /// InfrastructureVmWriteTests) — not previously exercised for the Environment PUT path.</summary>
    [TestMethod]
    public async Task Put_BlankDisplayName_Returns400()
    {
        var client = await Fx.CreateAuthenticatedClientAsync("admin@env-put-blank-name.test");
        var created = await RegisterEnvAsync(client, "Blank Name Base");

        var resp = await client.SendAsync(NewPut(created.Id, created.Version, EditFrom(created, displayName: "")));

        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [TestMethod]
    public async Task Put_UnknownId_Returns404()
    {
        var client = await Fx.CreateAuthenticatedClientAsync("admin@env-put-unknown.test");
        var created = await RegisterEnvAsync(client, "Unknown Base");

        var resp = await client.SendAsync(NewPut(Guid.NewGuid(), created.Version, EditFrom(created)));

        Assert.AreEqual(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [TestMethod]
    public async Task Put_CrossTenant_Returns404()
    {
        var orgA = await Fx.CreateAuthenticatedClientAsync("admin@env-put-tenant-a.test");
        var created = await RegisterEnvAsync(orgA, "CrossTenant Env");

        var orgB = await Fx.CreateAuthenticatedClientAsync("admin@env-put-tenant-b.test");
        var resp = await orgB.SendAsync(NewPut(created.Id, created.Version, EditFrom(created, displayName: "Hijack")));

        Assert.AreEqual(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [TestMethod]
    public async Task Put_WritesEnvironmentEditedAuditRow()
    {
        var tenantId = Fx.TenantIdForEmail("admin@env-put-audit.test");
        var client = await Fx.CreateAuthenticatedClientAsync("admin@env-put-audit.test");
        var created = await RegisterEnvAsync(client, "Audit Edit Env");

        var resp = await client.SendAsync(NewPut(created.Id, created.Version, EditFrom(created, displayName: "Audit Edit Env v2")));
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode, $"EditEnvironment failed: {await resp.Content.ReadAsStringAsync()}");

        var rows = await Fx.ReadAuditLogAsync(tenantId.Value);
        var row = rows.Single(r =>
            r.Action == CatalogAuditActions.EnvironmentEdited &&
            r.TargetId == created.Id.ToString());
        Assert.AreEqual(CatalogAuditTargetTypes.Environment, row.TargetType);
        using var data = JsonDocument.Parse(row.DataJson!);
        Assert.AreEqual("Audit Edit Env v2", data.RootElement.GetProperty("displayName").GetString());
    }

    [TestMethod]
    public async Task Delete_RemovesEnvironment_Returns204()
    {
        var client = await Fx.CreateAuthenticatedClientAsync("admin@env-delete.test");
        var created = await RegisterEnvAsync(client, "Delete Env");

        var del = await client.SendAsync(NewDelete(created.Id, created.Version));
        Assert.AreEqual(HttpStatusCode.NoContent, del.StatusCode, $"DeleteEnvironment failed: {await del.Content.ReadAsStringAsync()}");

        var get = await client.GetAsync($"/api/v1/catalog/environments/{created.Id}");
        Assert.AreEqual(HttpStatusCode.NotFound, get.StatusCode);
    }

    [TestMethod]
    public async Task Delete_WritesEnvironmentDeletedAuditRow()
    {
        var tenantId = Fx.TenantIdForEmail("admin@env-delete-audit.test");
        var client = await Fx.CreateAuthenticatedClientAsync("admin@env-delete-audit.test");
        var created = await RegisterEnvAsync(client, "Audit Delete Env");

        var del = await client.SendAsync(NewDelete(created.Id, created.Version));
        Assert.AreEqual(HttpStatusCode.NoContent, del.StatusCode, $"DeleteEnvironment failed: {await del.Content.ReadAsStringAsync()}");

        var rows = await Fx.ReadAuditLogAsync(tenantId.Value);
        var row = rows.Single(r =>
            r.Action == CatalogAuditActions.EnvironmentDeleted &&
            r.TargetId == created.Id.ToString());
        Assert.AreEqual(CatalogAuditTargetTypes.Environment, row.TargetType);
    }

    [TestMethod]
    public async Task Delete_NonOrgAdmin_Returns403()
    {
        var client = await Fx.CreateAuthenticatedClientAsync("admin@env-delete-noperm.test");
        var created = await RegisterEnvAsync(client, "Delete NoPerm Env");

        // Member carries CatalogEnvironmentsRegister but NOT CatalogEnvironmentsDelete
        // (OrgAdmin-only) — proves the delete gate is a distinct, narrower permission.
        var member = await Fx.CreateAuthenticatedClientAsync(
            "member-env-delete@env-delete-noperm.test", new[] { KartovaRoles.Member });

        var resp = await member.SendAsync(NewDelete(created.Id, created.Version));

        Assert.AreEqual(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [TestMethod]
    public async Task Delete_StaleIfMatch_Returns412()
    {
        var client = await Fx.CreateAuthenticatedClientAsync("admin@env-delete-stale.test");
        var created = await RegisterEnvAsync(client, "Delete Stale Env");

        var put = await client.SendAsync(NewPut(created.Id, created.Version, EditFrom(created, displayName: "Delete Stale Env v2")));
        Assert.AreEqual(HttpStatusCode.OK, put.StatusCode);
        var putBody = await put.Content.ReadFromJsonAsync<EnvironmentDetailResponse>(KartovaApiFixtureBase.WireJson);

        var del = await client.SendAsync(NewDelete(created.Id, created.Version));
        Assert.AreEqual(HttpStatusCode.PreconditionFailed, del.StatusCode);

        // gate-7 T2 equivalent: currentVersion capture works end-to-end on the DELETE
        // path too, not just PUT (Put_StaleIfMatch_Returns412 covers PUT).
        var problem = await del.Content.ReadFromJsonAsync<ProblemPayload>();
        Assert.AreEqual(putBody!.Version, problem!.CurrentVersion);
    }

    [TestMethod]
    public async Task Delete_MissingIfMatch_Returns428()
    {
        var client = await Fx.CreateAuthenticatedClientAsync("admin@env-delete-noifmatch.test");
        var created = await RegisterEnvAsync(client, "Delete NoIfMatch Env");

        var resp = await client.SendAsync(NewDelete(created.Id, ifMatch: null));

        Assert.AreEqual(HttpStatusCode.PreconditionRequired, resp.StatusCode);
    }

    [TestMethod]
    public async Task Delete_UnknownId_Returns404()
    {
        var client = await Fx.CreateAuthenticatedClientAsync("admin@env-delete-unknown.test");
        var created = await RegisterEnvAsync(client, "Delete Unknown Base");

        var resp = await client.SendAsync(NewDelete(Guid.NewGuid(), created.Version));

        Assert.AreEqual(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [TestMethod]
    public async Task Delete_CrossTenant_Returns404()
    {
        var orgA = await Fx.CreateAuthenticatedClientAsync("admin@env-delete-tenant-a.test");
        var created = await RegisterEnvAsync(orgA, "Delete CrossTenant Env");

        var orgB = await Fx.CreateAuthenticatedClientAsync("admin@env-delete-tenant-b.test");
        var resp = await orgB.SendAsync(NewDelete(created.Id, created.Version));

        Assert.AreEqual(HttpStatusCode.NotFound, resp.StatusCode);
    }

    // Minimal typed-extension helper for the 412 ProblemDetails body — mirrors
    // InfrastructureVmWriteTests.ProblemPayload. System.Text.Json deserialises the flat
    // RFC 7807 extension member `currentVersion` by name.
    private sealed class ProblemPayload
    {
        public string Type { get; set; } = string.Empty;
        public string? CurrentVersion { get; set; }
    }

    /// <summary>Fires <paramref name="raceAction"/> exactly once, the first time <paramref
    /// name="shouldFire"/> matches the pending change set — a deterministic (no threads, no
    /// Task.WhenAll) reproduction of a concurrent writer landing between this handler's initial
    /// read and its own SaveChangesAsync reaching Postgres. Mirrors
    /// <c>SetComponentSystemTests.RaceOnSaveInterceptor</c> exactly.</summary>
    private sealed class RaceOnSaveInterceptor(Func<ChangeTracker, bool> shouldFire, Func<Task> raceAction)
        : SaveChangesInterceptor
    {
        private bool _fired;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!_fired && eventData.Context is { } ctx && shouldFire(ctx.ChangeTracker))
            {
                _fired = true;
                await raceAction();
            }
            return await base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed class FakeTenantContext(TenantId id) : ITenantContext
    {
        public TenantId Id { get; } = id;
        public bool IsTenantScoped => true;
        public IReadOnlyCollection<string> Roles => [];
        public IReadOnlyList<TeamMembershipInfo> TeamMemberships => [];
        public IReadOnlySet<Guid> TeamIds { get; } = new HashSet<Guid>();
        public void Populate(TenantId id, IReadOnlyCollection<string> roles) { }
        public void PopulateTeamMemberships(IReadOnlyList<TeamMembershipInfo> memberships) { }
        public void Clear() { }
    }

    private sealed class FakeCurrentUser : ICurrentUser
    {
        public Guid UserId => Guid.NewGuid();
        public string DisplayName => "race-test-actor";
        public IReadOnlyList<TeamMembershipInfo> TeamMemberships => [];
        public IReadOnlySet<Guid> TeamIds { get; } = new HashSet<Guid>();
    }

    private sealed class NoOpAuditWriter : IAuditWriter
    {
        public Task AppendAsync(AuditEntry entry, CancellationToken ct) => Task.CompletedTask;
        public Task AppendSystemAsync(TenantId tenant, AuditEntry entry, CancellationToken ct) => Task.CompletedTask;
    }
}
