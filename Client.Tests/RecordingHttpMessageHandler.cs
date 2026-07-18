using System.Net;
using System.Text;

namespace Client.Tests;

/// <summary>
/// Fake transport used to test <see cref="Client.Infrastructure.ClientGateway"/> without
/// any real network access: every request is recorded (so tests can assert on the
/// $filter/$expand/$orderby/$top/$skip the query builder produced) and answered with a
/// canned response supplied by the test.
/// </summary>
public sealed class RecordingHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
    private readonly List<Uri> _requestUris = [];

    public RecordingHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        _respond = respond;

    public IReadOnlyList<Uri> RequestUris => _requestUris;

    public static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json) => new(statusCode)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _requestUris.Add(request.RequestUri!);
        return Task.FromResult(_respond(request));
    }
}
