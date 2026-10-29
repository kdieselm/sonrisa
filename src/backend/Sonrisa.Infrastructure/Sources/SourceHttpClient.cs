using System.Net;
using Sonrisa.Infrastructure.Configuration;

namespace Sonrisa.Infrastructure.Sources;

/// <summary>
/// The one long-lived <see cref="HttpClient"/> shared by the polled sources. Long-lived clients are safe when the
/// handler recycles its connections (<see cref="SocketsHttpHandler.PooledConnectionLifetime"/>), so DNS changes are
/// picked up; this keeps Infrastructure free of <c>IHttpClientFactory</c> and of a new package (D25).
/// </summary>
public sealed class SourceHttpClient : IDisposable
{
    public SourceHttpClient(SourcesOptions options)
    {
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            AutomaticDecompression = DecompressionMethods.All,
        };
        Http = new HttpClient(handler)
        {
            Timeout = options.HttpTimeout,
            MaxResponseContentBufferSize = options.MaxResponseBytes,
        };
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("Sonrisa/0.1 (+demo alert app)");
    }

    public HttpClient Http { get; }

    public void Dispose() => Http.Dispose();
}
