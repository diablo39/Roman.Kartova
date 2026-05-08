using System.Text.RegularExpressions;
using Kartova.SharedKernel;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kartova.SharedKernel.Tests;

[TestClass]
public class KartovaConnectionStringsTests
{
    [TestMethod]
    public void Require_returns_value_when_connection_string_is_present()
    {
        var config = BuildConfig(("ConnectionStrings:Kartova", "Host=db;Database=k"));

        var cs = KartovaConnectionStrings.Require(config, KartovaConnectionStrings.Main);

        Assert.AreEqual("Host=db;Database=k", cs);
    }

    [TestMethod]
    public void Require_throws_with_canonical_message_when_missing()
    {
        var config = BuildConfig();

        var ex = Assert.ThrowsExactly<InvalidOperationException>(
            () => KartovaConnectionStrings.Require(config, KartovaConnectionStrings.Main));

        // Stable diagnostic shape — Program.cs and module RegisterForMigrator
        // calls all surface this message; CI logs scrape it on bootstrap failures.
        Assert.AreEqual(
            "Connection string 'Kartova' is required. Set it via ConnectionStrings__Kartova env var.",
            ex.Message);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public void Require_throws_when_connection_string_is_blank(string blank)
    {
        var config = BuildConfig(("ConnectionStrings:Kartova", blank));

        var ex = Assert.ThrowsExactly<InvalidOperationException>(
            () => KartovaConnectionStrings.Require(config, KartovaConnectionStrings.Main));

        StringAssert.Matches(ex.Message, new Regex("Kartova.*required"));
    }

    [TestMethod]
    public void RequireMain_resolves_against_Kartova_key()
    {
        var config = BuildConfig(("ConnectionStrings:Kartova", "main-cs"));

        Assert.AreEqual("main-cs", KartovaConnectionStrings.RequireMain(config));
    }

    [TestMethod]
    public void RequireBypass_resolves_against_KartovaBypass_key()
    {
        var config = BuildConfig(("ConnectionStrings:KartovaBypass", "bypass-cs"));

        Assert.AreEqual("bypass-cs", KartovaConnectionStrings.RequireBypass(config));
    }

    private static IConfiguration BuildConfig(params (string Key, string Value)[] entries) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(entries.Select(e => new KeyValuePair<string, string?>(e.Key, e.Value)))
            .Build();
}
