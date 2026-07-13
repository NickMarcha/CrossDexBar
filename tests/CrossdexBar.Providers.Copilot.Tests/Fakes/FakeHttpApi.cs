using CrossdexBar.Core.Host;

namespace CrossdexBar.Providers.Copilot.Tests.Fakes;

internal sealed class FakeHttpApi(Func<HttpRequestMessage, HttpResponseMessage> respond) : IHttpApi
{
    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct = default) =>
        Task.FromResult(respond(request));
}
