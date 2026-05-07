using FluentAssertions;
using Kartova.Catalog.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.InMemory.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

// NOTE: The same alias trick as ApplicationTests.cs — `Kartova.Catalog.Application`
// namespace wins simple-name lookup, so we alias the domain type explicitly.
using DomainApplication = Kartova.Catalog.Domain.Application;

namespace Kartova.Catalog.Tests;

/// <summary>
/// Pins the EF Core model shape produced by <see cref="EfApplicationConfiguration"/>.
/// These tests introspect the compiled EF model via an InMemory context so that
/// removing or reordering any Fluent API call in the configuration class causes an
/// immediate failure — without requiring a live database.
/// </summary>
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

    [Fact]
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
        pk.Should().NotBeNull("a primary key must be configured");
        pk!.Properties.Should().ContainSingle(p => p.Name == "_id",
            "_id backing field must be the sole primary key property");

        // The strongest kill: explicit HasKey → ConfigurationSource.Explicit.
        pk.GetConfigurationSource().Should().Be(ConfigurationSource.Explicit,
            "the PK must be configured via explicit HasKey on the _id field");
    }

    // -----------------------------------------------------------------------
    // Complementary assertions — kill neighbouring Statement mutations
    // -----------------------------------------------------------------------

    [Fact]
    public void Configure_maps_to_catalog_applications_table()
    {
        // Arrange / Act
        var entity = GetEntityType();

        // Assert
        entity.GetTableName().Should().Be("catalog_applications");
    }

    [Fact]
    public void Configure_value_generation_never_on_Id()
    {
        // Arrange / Act — the PK is now the _id backing field (plain Guid)
        var idProp = GetEntityType().FindProperty("_id")!;

        // Assert
        idProp.ValueGenerated.Should().Be(ValueGenerated.Never,
            "ApplicationId values are assigned by the domain, not the database");
    }

    [Fact]
    public void Configure_Id_column_name_is_id()
    {
        // Arrange / Act — the PK is now the _id backing field (plain Guid)
        var idProp = GetEntityType().FindProperty("_id")!;

        // Assert
        idProp.GetColumnName().Should().Be("id");
    }

    [Fact]
    public void Configure_pins_required_columns_and_max_lengths()
    {
        // Arrange
        var entity = GetEntityType();

        // Act / Assert — Name
        var name = entity.FindProperty("Name")!;
        name.IsNullable.Should().BeFalse("Name is required");
        name.GetMaxLength().Should().Be(256, "Name max length is 256");
        name.GetColumnName().Should().Be("name");

        // DisplayName
        var displayName = entity.FindProperty("DisplayName")!;
        displayName.IsNullable.Should().BeFalse("DisplayName is required");
        displayName.GetMaxLength().Should().Be(128, "DisplayName max length is 128");
        displayName.GetColumnName().Should().Be("display_name");

        // Description
        var description = entity.FindProperty("Description")!;
        description.IsNullable.Should().BeFalse("Description is required");
        description.GetColumnName().Should().Be("description");

        // OwnerUserId
        var ownerUserId = entity.FindProperty("OwnerUserId")!;
        ownerUserId.IsNullable.Should().BeFalse("OwnerUserId is required");
        ownerUserId.GetColumnName().Should().Be("owner_user_id");

        // TenantId
        var tenantId = entity.FindProperty("TenantId")!;
        tenantId.IsNullable.Should().BeFalse("TenantId is required");
        tenantId.GetColumnName().Should().Be("tenant_id");

        // CreatedAt
        var createdAt = entity.FindProperty("CreatedAt")!;
        createdAt.IsNullable.Should().BeFalse("CreatedAt is required");
        createdAt.GetColumnName().Should().Be("created_at");
    }

    [Fact]
    public void Configure_ignores_domain_typed_Id_property()
    {
        // Arrange — use the convention model so we can read IgnoredMembers (which is
        // erased once the model is finalized). Apply the configuration to a fresh
        // ModelBuilder, then inspect the underlying convention entity type.
        var conventionSet = InMemoryConventionSetBuilder.Build();
        var modelBuilder = new ModelBuilder(conventionSet);
        new EfApplicationConfiguration().Configure(modelBuilder.Entity<DomainApplication>());

        var conventionEntity = (IConventionEntityType)modelBuilder.Model
            .FindEntityType(typeof(DomainApplication))!;

        // Act
        var ignoredMembers = conventionEntity.GetIgnoredMembers();
        var finalEntity = GetEntityType();
        var idProperty = finalEntity.FindProperty(nameof(DomainApplication.Id));
        var mappedNames = finalEntity.GetProperties().Select(p => p.Name).ToList();

        // Assert
        // Primary kill: explicit Ignore registers the member name in IgnoredMembers.
        ignoredMembers.Should().Contain(nameof(DomainApplication.Id),
            "Configure must explicitly Ignore the domain-typed Id getter so future EF " +
            "convention changes (complex/owned-type auto-mapping) cannot silently map it");

        // Secondary kill: the finalized model must not surface Id as a mapped property —
        // only the shadow _id backing field should appear.
        idProperty.Should().BeNull("the domain-typed Id getter must not be mapped");
        mappedNames.Should().NotContain(nameof(DomainApplication.Id));
        mappedNames.Should().Contain("_id", "the backing field _id must be mapped instead");
    }

    [Fact]
    public void Configure_indexes_tenant_id_with_named_index()
    {
        // Arrange / Act
        var entity = GetEntityType();

        // Assert
        var index = entity.GetIndexes()
            .SingleOrDefault(i => i.Properties.Any(p => p.Name == "TenantId"));

        index.Should().NotBeNull("TenantId must have a dedicated index");
        index!.GetDatabaseName().Should().Be("ix_catalog_applications_tenant_id");
    }
}
