namespace Kartova.SharedKernel.Identity.Tests;

internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();
    public List<HttpRequestMessage> Captured { get; } = new();
    public List<string?> CapturedBodies { get; } = new();

    /// <summary>
    /// Opt-in switch for body capture. When <c>false</c> (the default) the stub
    /// does NOT call <c>request.Content.ReadAsStringAsync</c>, so callers using
    /// non-rewindable content (e.g. <see cref="StreamContent"/>) are not silently
    /// drained by the test infrastructure. When <c>true</c> the captured body is
    /// pushed into <see cref="CapturedBodies"/> at the same index as the matching
    /// <see cref="Captured"/> request; with capture disabled the corresponding
    /// <see cref="CapturedBodies"/> slot is <c>null</c> so the two lists stay
    /// length-aligned.
    /// </summary>
    public bool CaptureBodies { get; init; } = false;

    public void EnqueueResponse(Func<HttpRequestMessage, HttpResponseMessage> factory) => _responses.Enqueue(factory);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Captured.Add(request);
        // When body capture is enabled we must read BEFORE returning — production code disposes
        // the request (and thus its content) when its `using` scope exits, so the content is
        // unreadable from test code afterwards.
        if (CaptureBodies && request.Content is not null)
        {
            CapturedBodies.Add(await request.Content.ReadAsStringAsync(ct));
        }
        else
        {
            CapturedBodies.Add(null);
        }
        if (_responses.Count == 0)
            throw new InvalidOperationException($"No stubbed response for {request.Method} {request.RequestUri}");
        return _responses.Dequeue()(request);
    }
}
