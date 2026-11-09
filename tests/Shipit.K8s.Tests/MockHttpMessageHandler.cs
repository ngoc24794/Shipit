using System.Net;
using System.Text;
using System.Text.Json;
using k8s;

namespace Shipit.K8s.Tests;

public sealed class RecordedRequest
{
    public required HttpMethod Method { get; init; }
    public required Uri RequestUri { get; init; }
    public string? ContentType { get; init; }
    public string? ContentBody { get; init; }
}

public sealed class MockHttpMessageHandler : DelegatingHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseProvider;
    private readonly List<RecordedRequest> _requests = new();

    public IReadOnlyList<RecordedRequest> Requests => _requests;

    public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseProvider)
    {
        _responseProvider = responseProvider;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string? body = null;
        string? contentType = null;
        if (request.Content != null)
        {
            contentType = request.Content.Headers.ContentType?.ToString();
            body = await request.Content.ReadAsStringAsync(cancellationToken);
        }

        _requests.Add(new RecordedRequest
        {
            Method = request.Method,
            RequestUri = request.RequestUri!,
            ContentType = contentType,
            ContentBody = body,
        });

        var response = _responseProvider(request);
        response.RequestMessage = request;
        return response;
    }

    public static IKubernetes CreateClient(MockHttpMessageHandler handler)
    {
        var config = new KubernetesClientConfiguration { Host = "http://localhost" };
        return new Kubernetes(config, handler);
    }

    public static HttpResponseMessage JsonResponse<T>(T payload, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var json = JsonSerializer.Serialize(payload);
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    public static HttpResponseMessage ErrorResponse(HttpStatusCode statusCode, string reasonPhrase, string message)
    {
        var json = JsonSerializer.Serialize(new
        {
            kind = "Status",
            apiVersion = "v1",
            status = "Failure",
            message,
            reason = statusCode.ToString(),
            code = (int)statusCode
        });

        return new HttpResponseMessage(statusCode)
        {
            ReasonPhrase = reasonPhrase,
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}
