using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Mnemotron.Data.ClickHouse.ADO;
using NUnit.Framework;

namespace Mnemotron.Data.ClickHouse.Tests.ADO;

// No network access: a fake HttpMessageHandler stands in for the ClickHouse
// server so these tests only exercise header construction, not the wire
// protocol. Injected via the ClickHouseConnection(string, HttpClient) ctor.
public class UserAgentTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestHeaders LastRequestHeaders { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestHeaders = request.Headers;

            // Body shape expected by ClickHouseConnection.OpenAsync: "version\ttimezone"
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("24.8.11.51\tUTC", Encoding.UTF8),
            };
            return Task.FromResult(response);
        }
    }

    [Test]
    public async Task ShouldSendProductUserAgent()
    {
        using var handler = new CapturingHandler();
        using var httpClient = new HttpClient(handler);
        using var connection = new ClickHouseConnection(TestUtilities.GetConnectionStringBuilder().ToString(), httpClient);

        await connection.OpenAsync();

        Assert.That(handler.LastRequestHeaders, Is.Not.Null);
        Assert.That(handler.LastRequestHeaders.UserAgent.ToString(), Does.StartWith("Mnemotron.Data.ClickHouse/"));
    }

    [Test]
    public async Task ShouldNotClobberUserSuppliedUserAgent()
    {
        using var handler = new CapturingHandler();
        using var httpClient = new HttpClient(handler);
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("MyApp/1.0");
        using var connection = new ClickHouseConnection(TestUtilities.GetConnectionStringBuilder().ToString(), httpClient);

        await connection.OpenAsync();

        // request.Headers reflects the fully merged header set that HttpClient
        // actually sends (DefaultRequestHeaders merged in for anything the
        // provider didn't set on the request itself).
        Assert.That(handler.LastRequestHeaders, Is.Not.Null);
        Assert.That(handler.LastRequestHeaders.UserAgent.ToString(), Is.EqualTo("MyApp/1.0"));
    }
}
