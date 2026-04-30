using Microsoft.Extensions.Configuration;

namespace Kartova.SharedKernel;

/// <summary>
/// Canonical connection-string names used across the solution. The string
/// values are also the configuration keys under <c>ConnectionStrings:*</c>
/// (so the env-var form is <c>ConnectionStrings__Kartova</c> /
/// <c>ConnectionStrings__KartovaBypass</c>).
/// </summary>
public static class KartovaConnectionStrings
{
    public const string Main = "Kartova";
    public const string Bypass = "KartovaBypass";

    /// <summary>
    /// Resolves the connection string at <c>ConnectionStrings:<paramref name="name"/></c>
    /// or throws an <see cref="InvalidOperationException"/> with a uniform diagnostic
    /// message naming the configuration key in its env-var form. Use this anywhere a
    /// connection string is mandatory at startup so the error shape stays consistent.
    /// </summary>
    public static string Require(IConfiguration configuration, string name)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var value = configuration[$"ConnectionStrings:{name}"];
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"Connection string '{name}' is required. Set it via ConnectionStrings__{name} env var.");
        }
        return value;
    }

    /// <summary>Convenience wrapper for the tenant-scoped main connection (<see cref="Main"/>).</summary>
    public static string RequireMain(IConfiguration configuration) => Require(configuration, Main);

    /// <summary>Convenience wrapper for the BYPASSRLS admin connection (<see cref="Bypass"/>).</summary>
    public static string RequireBypass(IConfiguration configuration) => Require(configuration, Bypass);
}
