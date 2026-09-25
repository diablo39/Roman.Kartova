using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kartova.SharedKernel.AspNetCore.Tests;

/// <summary>
/// Unit-level coverage for <see cref="ConcurrencyTokenCapture.SetExpectedVersion"/>'s two
/// guard clauses (TD-012 gate-7 finding, raised independently by code-reviewer,
/// pr-test-analyzer, and silent-failure-hunter): the happy path is already covered end-to-end
/// by each migrated handler's real-seam 412 integration test, but no test exercised the
/// missing-token or wrong-CLR-type throws directly. Uses a throwaway in-memory EF model — the
/// class under test only reads model metadata and sets <c>OriginalValue</c>; it never calls
/// <c>SaveChangesAsync</c>, so no real database is involved.
/// </summary>
[TestClass]
public sealed class ConcurrencyTokenCaptureTests
{
    private sealed class UintTokenEntity
    {
        public int Id { get; set; }
        public uint Version { get; set; }
    }

    private sealed class NoTokenEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    private sealed class NonUintTokenEntity
    {
        public int Id { get; set; }
        public byte[] RowVersion { get; set; } = [];
    }

    private sealed class TestDbContext(DbContextOptions<TestDbContext> opts) : DbContext(opts)
    {
        public DbSet<UintTokenEntity> UintTokenEntities => Set<UintTokenEntity>();
        public DbSet<NoTokenEntity> NoTokenEntities => Set<NoTokenEntity>();
        public DbSet<NonUintTokenEntity> NonUintTokenEntities => Set<NonUintTokenEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<UintTokenEntity>()
                .Property(e => e.Version).IsConcurrencyToken();
            modelBuilder.Entity<NonUintTokenEntity>()
                .Property(e => e.RowVersion).IsRowVersion();
        }
    }

    private static TestDbContext NewContext() =>
        new(new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static EntityEntry Track<T>(TestDbContext db, T entity) where T : class =>
        db.Attach(entity);

    [TestMethod]
    public void Sets_OriginalValue_on_the_uint_concurrency_token()
    {
        using var db = NewContext();
        var entry = Track(db, new UintTokenEntity { Id = 1, Version = 1 });

        ConcurrencyTokenCapture.SetExpectedVersion(entry, 7);

        Assert.AreEqual(7u, entry.Property(nameof(UintTokenEntity.Version)).OriginalValue);
    }

    [TestMethod]
    public void Throws_when_entity_has_no_concurrency_token_property()
    {
        using var db = NewContext();
        var entry = Track(db, new NoTokenEntity { Id = 1, Name = "x" });

        var ex = Assert.ThrowsExactly<InvalidOperationException>(
            () => ConcurrencyTokenCapture.SetExpectedVersion(entry, 1));
        StringAssert.Contains(ex.Message, nameof(NoTokenEntity));
    }

    [TestMethod]
    public void Throws_when_concurrency_token_is_not_uint()
    {
        using var db = NewContext();
        var entry = Track(db, new NonUintTokenEntity { Id = 1, RowVersion = [] });

        var ex = Assert.ThrowsExactly<InvalidOperationException>(
            () => ConcurrencyTokenCapture.SetExpectedVersion(entry, 1));
        StringAssert.Contains(ex.Message, nameof(NonUintTokenEntity.RowVersion));
    }
}
