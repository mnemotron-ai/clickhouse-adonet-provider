using System;
using System.Net;
using System.Net.Http;

namespace Mnemotron.Data.ClickHouse.Http;

internal class DefaultPoolHttpClientFactory : IHttpClientFactory
{
    private static readonly HttpClientHandler DefaultHttpClientHandler = new() { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate };

#if NETFRAMEWORK
    // The handler is shared process-wide, and on .NET Framework the legacy
    // stack caps a client process at ServicePointManager.DefaultConnectionLimit
    // (historically 2) concurrent connections per host — serializing parallel
    // SSIS dataflows against the same ClickHouse. .NET 8's SocketsHttpHandler
    // has no such cap, so this is a net4x-only lift.
    // ponytail: fixed 16, make it a connection-string knob if a real workload asks
    static DefaultPoolHttpClientFactory()
    {
        try
        {
            DefaultHttpClientHandler.MaxConnectionsPerServer = 16;
        }
        catch (PlatformNotSupportedException)
        {
            ServicePointManager.DefaultConnectionLimit = 16;
        }
    }
#endif

    public TimeSpan Timeout { get; init; }

    public HttpClient CreateClient(string name) => new(DefaultHttpClientHandler, false) { Timeout = Timeout };
}
