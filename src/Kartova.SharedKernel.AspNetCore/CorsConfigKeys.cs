namespace Kartova.SharedKernel.AspNetCore;

public static class CorsConfigKeys
{
    public const string Section = "Cors";
    public const string AllowedOrigins = $"{Section}:AllowedOrigins";

    /// <summary>ADR-0118: browser origins of the platform-operator console (web-admin). Disjoint from <see cref="AllowedOrigins"/>.</summary>
    public const string AdminAllowedOrigins = $"{Section}:AdminAllowedOrigins";
}
