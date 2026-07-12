using System.Net.Http.Headers;

namespace CrossdexBar.Core.Host;

public sealed class HttpApi : IHttpApi, IDisposable
{
    private readonly HttpClient _client;

    public HttpApi(HttpClient? client = null)
    {
        _client = client ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        _client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("CrossdexBar", "0.1"));
    }

    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct = default) =>
        _client.SendAsync(request, ct);

    public void Dispose() => _client.Dispose();
}
