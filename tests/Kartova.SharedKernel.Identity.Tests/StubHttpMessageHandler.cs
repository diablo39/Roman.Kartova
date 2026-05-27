namespace Kartova.SharedKernel.Identity.Tests;

internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();
    public List<HttpRequestMessage> Captured { get; } = new();

    public void EnqueueResponse(Func<HttpRequestMessage, HttpResponseMessage> factory) => _responses.Enqueue(factory);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Captured.Add(request);
        if (_responses.Count == 0)
            throw new InvalidOperationException($"No stubbed response for {request.Method} {request.RequestUri}");
        return Task.FromResult(_responses.Dequeue()(request));
    }
}
