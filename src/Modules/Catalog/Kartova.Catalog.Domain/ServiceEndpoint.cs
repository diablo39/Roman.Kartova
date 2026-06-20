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
        // Require an absolute URI *with a host*. UriKind.Absolute alone is
        // platform-dependent for rooted paths: "/v1/orders" is non-absolute on
        // Windows but parses as a hostless file:// URI on Linux. The Authority
        // check rejects it consistently on both while accepting real network
        // endpoints (https://, grpc://, tcp://, ws(s)://, …).
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Authority))
            throw new ArgumentException("endpoint url must be an absolute URI with a host.", nameof(url));
        if (!Enum.IsDefined(protocol))
            throw new ArgumentException("unknown protocol.", nameof(protocol));

        Url = url;
        Protocol = protocol;
    }
}
