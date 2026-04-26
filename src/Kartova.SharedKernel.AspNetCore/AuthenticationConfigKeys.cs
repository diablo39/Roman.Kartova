namespace Kartova.SharedKernel.AspNetCore;

public static class AuthenticationConfigKeys
{
    public const string Section = "Authentication";
    public const string Authority = $"{Section}:Authority";
    public const string MetadataAddress = $"{Section}:MetadataAddress";
    public const string Audience = $"{Section}:Audience";
    public const string RequireHttpsMetadata = $"{Section}:RequireHttpsMetadata";
}
