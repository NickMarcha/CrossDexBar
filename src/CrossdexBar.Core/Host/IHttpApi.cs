namespace CrossdexBar.Core.Host;

/// <summary>
/// Thin wrapper around outbound HTTP so fetch strategies never construct their own <see cref="HttpClient"/>
/// (keeps timeouts/headers consistent and requests mockable in tests).
/// </summary>
public interface IHttpApi
{
    Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct = default);
}
