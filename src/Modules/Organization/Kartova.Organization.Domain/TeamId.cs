namespace Kartova.Organization.Domain;

public readonly record struct TeamId(Guid Value)
{
    public static TeamId New() => new(Guid.NewGuid());
}
