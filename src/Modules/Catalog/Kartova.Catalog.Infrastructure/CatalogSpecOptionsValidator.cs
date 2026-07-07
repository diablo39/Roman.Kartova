using Microsoft.Extensions.Options;

namespace Kartova.Catalog.Infrastructure;

/// <summary>Fails fast at startup if the configured spec cap is outside a safe band.
/// The streamed read buffers up to the cap, so an absurd value (e.g. 10 GB) is an
/// unbounded-memory vector — bound it to [1 KiB, 50 MiB].</summary>
public sealed class CatalogSpecOptionsValidator : IValidateOptions<CatalogSpecOptions>
{
    private const int Floor = 1024;                 // 1 KiB
    private const int Ceiling = 50 * 1024 * 1024;   // 50 MiB

    public ValidateOptionsResult Validate(string? name, CatalogSpecOptions options)
        => options.MaxContentBytes is >= Floor and <= Ceiling
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"{CatalogSpecOptions.SectionName}:MaxContentBytes must be between {Floor} and {Ceiling} bytes; got {options.MaxContentBytes}.");
}
