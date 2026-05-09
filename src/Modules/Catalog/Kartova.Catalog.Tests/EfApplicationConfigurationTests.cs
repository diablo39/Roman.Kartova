using Kartova.Catalog.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.InMemory.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

// NOTE: `Kartova.Catalog.Application` namespace wins simple-name lookup over
// `Kartova.Catalog.Domain.Application`, so we alias the domain type explicitly.
using DomainApplication = Kartova.Catalog.Domain.Application;

namespace Kartova.Catalog.Tests;

/// <summary>
/// Pins the EF Core model shape produced by <see cref="EfApplicationConfiguration"/>.
/// These tests introspect the compiled EF model via an InMemory context so that
/// removing or reordering any Fluent API call in the configuration class causes an
/// immediate failure — without requiring a live database.
/// </summary>
[TestClass]
public class EfApplicationConfigurationTests
{
    /// <summary>
    /// Returns a mutable (non-frozen) <see cref="IConventionEntityType"/> built by
    /// applying <see cref="EfApplicationConfiguration"/> to a fresh <see cref="ModelBuilder"/>.
    /// Using the mutable model (before <c>FinalizeModel()</c> is called) allows us to
    /// cast keys/properties to <see cref="IConventionKey"/> and read
    /// <see cref="ConfigurationSource"/>.
    /// </summary>
    private static IConventionEntityType BuildConventionModel()
    {
        // Build a convention set using the InMemory provider's static helper.
        var conventionSet = InMemoryConventionSetBuilder.Build();
        var modelBuilder = new ModelBuilder(conventionSet);

        // Apply only the configuration under test.
        new EfApplicationConfiguration().Configure(modelBuilder.Entity<DomainApplication>());

        return (IConventionEntityType)modelBuilder.Model
            .FindEntityType(typeof(DomainApplication))!;
    }

    private static IEntityType GetEntityType()
    {
        // Arrange — build an in-memory context to obtain the compiled model.
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase("EfApplicationConfigurationTests_" + Guid.NewGuid())
            .Options;

        using var ctx = new CatalogDbContext(options);

        return ctx.Model.FindEntityType(typeof(DomainApplication))
            ?? throw new InvalidOperationException("Application entity type not found in model.");
    }

    // -----------------------------------------------------------------------
    // Mutant-killing assertion (Statement_EfApplicationConfiguration.cs:13)
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Configure_marks_Id_as_primary_key()
    {
        // Arrange — use the mutable convention model so we can read ConfigurationSource.
        var entity = BuildConventionModel();

        // Act
        var pk = entity.FindPrimaryKey();

        // Assert
        // The PK is mapped via the private backing field _id (plain Guid).
        // Using a backing field instead of the ApplicationId-typed property allows
        // EF Core to translate ORDER BY / WHERE on the id column without going
        // through the value converter (which EF cannot push down to SQL).
        Assert.IsNotNull(pk, "a primary key must be configured");
        Assert.AreEqual(
            1,
            pk!.Properties.Count(p => p.Name == "_id"),
            "_id backing field must be the sole primary key property");

        // The strongest kill: explicit HasKey → ConfigurationSource.Explicit.
        Assert.AreEqual(
            ConfigurationSource.Explicit,
            pk.GetConfigurationSource(),
            "the PK must be configured via explicit HasKey on the _id field");
    }

    // -----------------------------------------------------------------------
    // Complementary assertions — kill neighbouring Statement mutations
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Configure_maps_to_catalog_applications_table()
    {
        // Arrange / Act
        var entity = GetEntityType();

        // Assert
        Assert.AreEqual("catalog_applications", entity.GetTableName());
    }

    [TestMethod]
    public void Configure_value_generation_never_on_Id()
    {
        // Arrange / Act — the PK is now the _id backing field (plain Guid)
        var idProp = GetEntityType().FindProperty("_id")!;

        // Assert
        Assert.AreEqual(
            ValueGenerated.Never,
            idProp.ValueGenerated,
            "ApplicationId values are assigned by the domain, not the database");
    }

    [TestMethod]
    public void Configure_Id_column_name_is_id()
    {
        // Arrange / Act — the PK is now the _id backing field (plain Guid)
        var idProp = GetEntityType().FindProperty("_id")!;

        // Assert
        Assert.AreEqual("id", idProp.GetColumnName());
    }

    [TestMethod]
    public void Configure_pins_required_columns_and_max_lengths()
    {
        // Arrange
        var entity = GetEntityType();

        // Act / Assert — Name
        var name = entity.FindProperty("Name")!;
        Assert.IsFalse(name.IsNullable, "Name is required");
        Assert.AreEqual(256, name.GetMaxLength(), "Name max length is 256");
        Assert.AreEqual("name", name.GetColumnName());

        // DisplayName
        var displayName = entity.FindProperty("DisplayName")!;
        Assert.IsFalse(displayName.IsNullable, "DisplayName is required");
        Assert.AreEqual(128, displayName.GetMaxLength(), "DisplayName max length is 128");
        Assert.AreEqual("display_name", displayName.GetColumnName());

        // Description
        var description = entity.FindProperty("Description")!;
        Assert.IsFalse(description.IsNullable, "Description is required");
        Assert.AreEqual("description", description.GetColumnName());

        // OwnerUserId
        var ownerUserId = entity.FindProperty("OwnerUserId")!;
        Assert.IsFalse(ownerUserId.IsNullable, "OwnerUserId is required");
        Assert.AreEqual("owner_user_id", ownerUserId.GetColumnName());

        // TenantId
        var tenantId = entity.FindProperty("TenantId")!;
        Assert.IsFalse(tenantId.IsNullable, "TenantId is required");
        Assert.AreEqual("tenant_id", tenantId.GetColumnName());

        // CreatedAt
        var createdAt = entity.FindProperty("CreatedAt")!;
        Assert.IsFalse(createdAt.IsNullable, "CreatedAt is required");
        Assert.AreEqual("created_at", createdAt.GetColumnName());
    }

    [TestMethod]
    public void Configure_ignores_domain_typed_Id_property()
    {
        // Arrange — use the convention model so we can read IgnoredMembers (which is
        // erased once the model is finalized).
        var conventionEntity = BuildConventionModel();

        // Act
        var ignoredMembers = conventionEntity.GetIgnoredMembers();
        var finalEntity = GetEntityType();
        var idProperty = finalEntity.FindProperty(nameof(DomainApplication.Id));
        var mappedNames = finalEntity.GetProperties().Select(p => p.Name).ToList();

        // Assert
        // Primary kill: explicit Ignore registers the member name in IgnoredMembers.
        Assert.IsTrue(
            ignoredMembers.Contains(nameof(DomainApplication.Id)),
            "Configure must explicitly Ignore the domain-typed Id getter so future EF " +
            "convention changes (complex/owned-type auto-mapping) cannot silently map it");

        // Secondary kill: the finalized model must not surface Id as a mapped property —
        // only the shadow _id backing field should appear.
        Assert.IsNull(idProperty, "the domain-typed Id getter must not be mapped");
        Assert.IsFalse(mappedNames.Contains(nameof(DomainApplication.Id)));
        Assert.IsTrue(mappedNames.Contains("_id"), "the backing field _id must be mapped instead");
    }

    [TestMethod]
    public void Configure_indexes_tenant_id_with_named_index()
    {
        // Arrange / Act
        var entity = GetEntityType();

        // Assert
        var index = entity.GetIndexes()
            .SingleOrDefault(i => i.Properties.Any(p => p.Name == "TenantId"));

        Assert.IsNotNull(index, "TenantId must have a dedicated index");
        Assert.AreEqual("ix_catalog_applications_tenant_id", index!.GetDatabaseName());
    }
}
