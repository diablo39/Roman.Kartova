namespace Kartova.SharedKernel.Identity.Tests;

internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();
    public List<HttpRequestMessage> Captured { get; } = new();
    public List<string?> CapturedBodies { get; } = new();

    public void EnqueueResponse(Func<HttpRequestMessage, HttpResponseMessage> factory) => _responses.Enqueue(factory);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Captured.Add(request);
        // Capture body BEFORE returning — production code disposes the request (and thus its content)
        // when its `using` scope exits, so the content is unreadable from test code afterwards.
        CapturedBodies.Add(request.Content is null ? null : await request.Content.ReadAsStringAsync(ct));
        if (_responses.Count == 0)
            throw new InvalidOperationException($"No stubbed response for {request.Method} {request.RequestUri}");
        return _responses.Dequeue()(request);
    }
}
