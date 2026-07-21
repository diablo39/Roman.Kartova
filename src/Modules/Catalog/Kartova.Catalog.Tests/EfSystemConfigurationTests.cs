using Kartova.Catalog.Domain;
using Kartova.Catalog.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.InMemory.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace Kartova.Catalog.Tests;

/// <summary>
/// Pins the EF Core model shape produced by <see cref="EfSystemConfiguration"/>
/// (mirrors <c>EfApplicationConfigurationTests</c>/<c>EfApiConfigurationTests</c>).
/// Introspects the compiled EF model via an InMemory context so that removing or
/// reordering any Fluent API call in the configuration class causes an immediate
/// failure — without requiring a live database.
/// </summary>
[TestClass]
public class EfSystemConfigurationTests
{
    private static IConventionEntityType BuildConventionModel()
    {
        var conventionSet = InMemoryConventionSetBuilder.Build();
        var modelBuilder = new ModelBuilder(conventionSet);

        new EfSystemConfiguration().Configure(modelBuilder.Entity<CatalogSystem>());

        return (IConventionEntityType)modelBuilder.Model
            .FindEntityType(typeof(CatalogSystem))!;
    }

    private static IEntityType GetEntityType()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase("EfSystemConfigurationTests_" + Guid.NewGuid())
            .Options;

        using var ctx = new CatalogDbContext(options);

        return ctx.Model.FindEntityType(typeof(CatalogSystem))
            ?? throw new InvalidOperationException("CatalogSystem entity type not found in model.");
    }

    [TestMethod]
    public void Configure_marks_Id_as_primary_key()
    {
        var entity = BuildConventionModel();

        var pk = entity.FindPrimaryKey();

        Assert.IsNotNull(pk, "a primary key must be configured");
        Assert.AreEqual(
            1,
            pk!.Properties.Count(p => p.Name == "_id"),
            "_id backing field must be the sole primary key property");

        Assert.AreEqual(
            ConfigurationSource.Explicit,
            pk.GetConfigurationSource(),
            "the PK must be configured via explicit HasKey on the _id field");
    }

    [TestMethod]
    public void Configure_maps_to_catalog_systems_table()
    {
        var entity = GetEntityType();

        Assert.AreEqual("catalog_systems", entity.GetTableName());
    }

    [TestMethod]
    public void Configure_value_generation_never_on_Id()
    {
        var idProp = GetEntityType().FindProperty("_id")!;

        Assert.AreEqual(
            ValueGenerated.Never,
            idProp.ValueGenerated,
            "CatalogSystemId values are assigned by the domain, not the database");
    }

    [TestMethod]
    public void Configure_Id_column_name_is_id()
    {
        var idProp = GetEntityType().FindProperty("_id")!;

        Assert.AreEqual("id", idProp.GetColumnName());
    }

    [TestMethod]
    public void Configure_pins_required_columns_and_max_lengths()
    {
        var entity = GetEntityType();

        var displayName = entity.FindProperty("DisplayName")!;
        Assert.IsFalse(displayName.IsNullable, "DisplayName is required");
        Assert.AreEqual(128, displayName.GetMaxLength(), "DisplayName max length is 128");
        Assert.AreEqual("display_name", displayName.GetColumnName());

        // Description is optional for System (unlike Api's required Description).
        var description = entity.FindProperty("Description")!;
        Assert.IsTrue(description.IsNullable, "Description is optional");
        Assert.AreEqual(4096, description.GetMaxLength(), "Description max length is 4096");
        Assert.AreEqual("description", description.GetColumnName());

        var createdByUserId = entity.FindProperty("CreatedByUserId")!;
        Assert.IsFalse(createdByUserId.IsNullable, "CreatedByUserId is required");
        Assert.AreEqual("created_by_user_id", createdByUserId.GetColumnName());

        var teamId = entity.FindProperty("TeamId")!;
        Assert.IsFalse(teamId.IsNullable, "TeamId is required (the steward team)");
        Assert.AreEqual("team_id", teamId.GetColumnName());

        var tenantId = entity.FindProperty("TenantId")!;
        Assert.IsFalse(tenantId.IsNullable, "TenantId is required");
        Assert.AreEqual("tenant_id", tenantId.GetColumnName());

        var createdAt = entity.FindProperty("CreatedAt")!;
        Assert.IsFalse(createdAt.IsNullable, "CreatedAt is required");
        Assert.AreEqual("created_at", createdAt.GetColumnName());
    }

    [TestMethod]
    public void Configure_ignores_domain_typed_Id_property()
    {
        var conventionEntity = BuildConventionModel();

        var ignoredMembers = conventionEntity.GetIgnoredMembers();
        var finalEntity = GetEntityType();
        var idProperty = finalEntity.FindProperty(nameof(CatalogSystem.Id));
        var mappedNames = finalEntity.GetProperties().Select(p => p.Name).ToList();

        Assert.IsTrue(
            ignoredMembers.Contains(nameof(CatalogSystem.Id)),
            "Configure must explicitly Ignore the domain-typed Id getter");

        Assert.IsNull(idProperty, "the domain-typed Id getter must not be mapped");
        Assert.IsFalse(mappedNames.Contains(nameof(CatalogSystem.Id)));
        Assert.IsTrue(mappedNames.Contains("_id"), "the backing field _id must be mapped instead");
    }

    [TestMethod]
    public void Configure_indexes_tenant_id_with_named_index()
    {
        var entity = GetEntityType();

        var index = entity.GetIndexes()
            .SingleOrDefault(i => i.Properties.Count == 1 && i.Properties[0].Name == "TenantId");

        Assert.IsNotNull(index, "TenantId must have a dedicated single-column index");
        Assert.AreEqual("ix_catalog_systems_tenant_id", index!.GetDatabaseName());
    }

    [TestMethod]
    public void Configure_indexes_tenant_id_and_display_name_with_named_composite_index()
    {
        var entity = GetEntityType();

        var index = entity.GetIndexes()
            .SingleOrDefault(i =>
                i.Properties.Count == 2 &&
                i.Properties[0].Name == "TenantId" &&
                i.Properties[1].Name == "DisplayName");

        Assert.IsNotNull(index, "composite (TenantId, DisplayName) index must exist for displayName sort");
        Assert.AreEqual("ix_catalog_systems_tenant_id_display_name", index!.GetDatabaseName());
    }

    [TestMethod]
    public void Configure_indexes_team_id_with_named_index()
    {
        var entity = GetEntityType();

        var index = entity.GetIndexes()
            .SingleOrDefault(i => i.Properties.Count == 1 && i.Properties[0].Name == "TeamId");

        Assert.IsNotNull(index, "TeamId must have a dedicated index (steward-team lookups)");
        Assert.AreEqual("idx_catalog_systems_team", index!.GetDatabaseName());
    }

    [TestMethod]
    public void Configure_maps_Xmin_as_postgres_row_version_concurrency_token()
    {
        // NOTE: column-type ("xid") is a relational-only annotation and cannot be read
        // back through the InMemory provider's IEntityType — column name, concurrency
        // token, and value-generation strategy are still verifiable here.
        var entity = GetEntityType();

        var xmin = entity.FindProperty(nameof(CatalogSystem.Xmin))!;
        Assert.AreEqual("xmin", xmin.GetColumnName());
        Assert.IsTrue(xmin.IsConcurrencyToken, "Xmin must be the concurrency token");
        Assert.AreEqual(ValueGenerated.OnAddOrUpdate, xmin.ValueGenerated);
    }
}
