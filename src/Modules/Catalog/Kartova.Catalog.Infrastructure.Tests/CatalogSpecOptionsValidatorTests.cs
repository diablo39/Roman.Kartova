using Kartova.Catalog.Infrastructure;
using Microsoft.Extensions.Options;

namespace Kartova.Catalog.Infrastructure.Tests;

[TestClass]
public sealed class CatalogSpecOptionsValidatorTests
{
    private static ValidateOptionsResult Validate(int maxBytes)
        => new CatalogSpecOptionsValidator().Validate(null, new CatalogSpecOptions { MaxContentBytes = maxBytes });

    [TestMethod]
    public void Rejects_below_floor()
    {
        Assert.IsTrue(Validate(0).Failed);
        Assert.IsTrue(Validate(1023).Failed);
    }

    [TestMethod]
    public void Rejects_above_ceiling()
        => Assert.IsTrue(Validate(50 * 1024 * 1024 + 1).Failed);

    [TestMethod]
    public void Accepts_within_band()
    {
        Assert.IsTrue(Validate(1024).Succeeded);
        Assert.IsTrue(Validate(5 * 1024 * 1024).Succeeded);
        Assert.IsTrue(Validate(50 * 1024 * 1024).Succeeded);
    }

    [TestMethod]
    public void Default_option_value_is_five_mib_and_valid()
    {
        var opts = new CatalogSpecOptions();
        Assert.AreEqual(5 * 1024 * 1024, opts.MaxContentBytes);
        Assert.IsTrue(new CatalogSpecOptionsValidator().Validate(null, opts).Succeeded);
    }
}
