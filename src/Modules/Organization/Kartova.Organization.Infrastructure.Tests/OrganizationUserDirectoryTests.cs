using Kartova.Organization.Domain;
using Kartova.Organization.Infrastructure;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Organization.Infrastructure.Tests;

[TestClass]
public sealed class OrganizationUserDirectoryTests
{
    private static OrganizationDbContext NewInMemory(out TenantId tenant)
    {
        tenant = new TenantId(Guid.NewGuid());
        var opts = new DbContextOptionsBuilder<OrganizationDbContext>()
            .UseInMemoryDatabase($"directory-{Guid.NewGuid()}").Options;
        return new OrganizationDbContext(opts);
    }

    [TestMethod]
    public async Task GetAsync_returns_user_when_present()
    {
        await using var db = NewInMemory(out var tenant);
        var id = Guid.NewGuid();
        db.Users.Add(new User { Id = id, TenantId = tenant, Email = "a@b.c", DisplayName = "A B", CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var sut = new OrganizationUserDirectory(db);
        var result = await sut.GetAsync(id, CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual("A B", result!.DisplayName);
    }

    [TestMethod]
    public async Task GetAsync_returns_null_when_absent()
    {
        await using var db = NewInMemory(out _);
        var sut = new OrganizationUserDirectory(db);
        Assert.IsNull(await sut.GetAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [TestMethod]
    public async Task GetManyAsync_returns_only_matched_ids()
    {
        await using var db = NewInMemory(out var tenant);
        var id1 = Guid.NewGuid(); var id2 = Guid.NewGuid(); var id3 = Guid.NewGuid();
        db.Users.AddRange(
            new User { Id = id1, TenantId = tenant, Email = "1@x", DisplayName = "One", CreatedAt = DateTimeOffset.UtcNow },
            new User { Id = id2, TenantId = tenant, Email = "2@x", DisplayName = "Two", CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var sut = new OrganizationUserDirectory(db);
        var result = await sut.GetManyAsync(new[] { id1, id2, id3 }, CancellationToken.None);

        Assert.AreEqual(2, result.Count);
        Assert.IsTrue(result.ContainsKey(id1));
        Assert.IsTrue(result.ContainsKey(id2));
        Assert.IsFalse(result.ContainsKey(id3));
        Assert.AreEqual("One", result[id1].DisplayName);
        Assert.AreEqual("1@x", result[id1].Email);
        Assert.AreEqual("Two", result[id2].DisplayName);
        Assert.AreEqual("2@x", result[id2].Email);
    }

    [TestMethod]
    public async Task GetManyAsync_returns_empty_for_empty_input()
    {
        await using var db = NewInMemory(out var tenant);
        db.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenant,
            Email = "noise@x",
            DisplayName = "Noise",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var sut = new OrganizationUserDirectory(db);
        var result = await sut.GetManyAsync(Array.Empty<Guid>(), CancellationToken.None);

        Assert.AreEqual(0, result.Count);
    }
}
