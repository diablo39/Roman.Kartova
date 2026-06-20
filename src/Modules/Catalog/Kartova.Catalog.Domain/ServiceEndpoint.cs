namespace Kartova.Catalog.Domain;

/// <summary>
/// Value object: one network endpoint a service exposes, with its protocol.
/// Validated on construction; EF rehydrates it from jsonb via this same
/// constructor (param names match the Url/Protocol properties).
/// </summary>
public sealed record ServiceEndpoint
{
    public string Url { get; }
    public Protocol Protocol { get; }

    public ServiceEndpoint(string url, Protocol protocol)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("endpoint url must not be empty.", nameof(url));
        if (url.Length > 2048)
            throw new ArgumentException("endpoint url must be <= 2048 characters.", nameof(url));
        if (!Uri.TryCreate(url, UriKind.Absolute, out _))
            throw new ArgumentException("endpoint url must be an absolute URI.", nameof(url));
        if (!Enum.IsDefined(protocol))
            throw new ArgumentException("unknown protocol.", nameof(protocol));

        Url = url;
        Protocol = protocol;
    }
}
