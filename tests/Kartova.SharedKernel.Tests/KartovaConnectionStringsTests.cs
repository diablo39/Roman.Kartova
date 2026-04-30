using FluentAssertions;
using Kartova.SharedKernel;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Kartova.SharedKernel.Tests;

public class KartovaConnectionStringsTests
{
    [Fact]
    public void Require_returns_value_when_connection_string_is_present()
    {
        var config = BuildConfig(("ConnectionStrings:Kartova", "Host=db;Database=k"));

        var cs = KartovaConnectionStrings.Require(config, KartovaConnectionStrings.Main);

        cs.Should().Be("Host=db;Database=k");
    }

    [Fact]
    public void Require_throws_with_canonical_message_when_missing()
    {
        var config = BuildConfig();

        var act = () => KartovaConnectionStrings.Require(config, KartovaConnectionStrings.Main);

        act.Should().Throw<InvalidOperationException>()
            // Stable diagnostic shape — Program.cs and module RegisterForMigrator
            // calls all surface this message; CI logs scrape it on bootstrap failures.
            .WithMessage("Connection string 'Kartova' is required. Set it via ConnectionStrings__Kartova env var.");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Require_throws_when_connection_string_is_blank(string blank)
    {
        var config = BuildConfig(("ConnectionStrings:Kartova", blank));

        var act = () => KartovaConnectionStrings.Require(config, KartovaConnectionStrings.Main);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Kartova*required*");
    }

    [Fact]
    public void RequireMain_resolves_against_Kartova_key()
    {
        var config = BuildConfig(("ConnectionStrings:Kartova", "main-cs"));

        KartovaConnectionStrings.RequireMain(config).Should().Be("main-cs");
    }

    [Fact]
    public void RequireBypass_resolves_against_KartovaBypass_key()
    {
        var config = BuildConfig(("ConnectionStrings:KartovaBypass", "bypass-cs"));

        KartovaConnectionStrings.RequireBypass(config).Should().Be("bypass-cs");
    }

    private static IConfiguration BuildConfig(params (string Key, string Value)[] entries) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(entries.Select(e => new KeyValuePair<string, string?>(e.Key, e.Value)))
            .Build();
}
