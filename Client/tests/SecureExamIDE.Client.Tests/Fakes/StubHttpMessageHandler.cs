using System.Net;
using System.Text;

namespace SecureExamIDE.Client.Tests.Fakes;

// Answers HTTP requests with queued responses and records what was sent, so the API client can be
// tested without a server.
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpResponseMessage>> _responses = new();

    public List<RecordedRequest> Requests { get; } = [];

    public void Respond(HttpStatusCode statusCode, string? json = null, string mediaType = "application/json") =>
        _responses.Enqueue(() => new HttpResponseMessage(statusCode)
        {
            Content = json is null ? new ByteArrayContent([]) : new StringContent(json, Encoding.UTF8, mediaType)
        });

    public void Fail(Exception exception) => _responses.Enqueue(() => throw exception);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string? body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

        Requests.Add(new RecordedRequest(
            request.Method,
            request.RequestUri!.AbsolutePath,
            request.RequestUri.Query,
            body,
            request.Headers.Authorization?.ToString()));

        return _responses.Dequeue()();
    }

    public sealed record RecordedRequest(HttpMethod Method, string Path, string Query, string? Body, string? Authorization);
}
