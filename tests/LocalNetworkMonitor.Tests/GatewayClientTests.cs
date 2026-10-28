using System.Net;
using System.Net.Http;
using System.Text;
using LocalNetworkMonitor.Core;
using Xunit;

namespace LocalNetworkMonitor.Tests;

public class GatewayClientTests
{
    [Fact]
    public async Task GatewayCountersProduceDevicePerspectiveDownloadsAndUploads()
    {
        using var handler = new ScriptedHandler(
            """addROD("hosts_trafstat", {"3600":{"02:00:00:00:00:01":{"bytes_rx":2500,"bytes_tx":5000}}});""");
        using var client = new GatewayClient(new HttpClient(handler) { BaseAddress = new("https://gateway.test") });
        var entry = Assert.Single(await client.ReadCountersAsync(default));
        Assert.Equal("02:00:00:00:00:01", entry.Id);
        Assert.Equal(2500UL, entry.DownloadBytes);
        Assert.Equal(5000UL, entry.UploadBytes);
    }

    [Theory]
    [InlineData("""addROD("hosts_trafstat", {"3600":{"02:00:00:00:00:01":{"bytes_rx":2500}}});""")]
    [InlineData("""addROD("hosts_trafstat", {"3600":{"02:00:00:00:00:01":{"bytes_rx":-1,"bytes_tx":0}}});""")]
    [InlineData("""addROD("hosts_trafstat", {"3600":{"02:00:00:00:00:01":{"bytes_rx":"bad","bytes_tx":0}}});""")]
    [InlineData("""addROD("hosts_trafstat", {"3600":{"invalid":{"bytes_rx":0,"bytes_tx":0}}});""")]
    [InlineData("""addROD("hosts_trafstat", {"3600":[]});""")]
    [InlineData("""addROD("hosts_trafstat", []);""")]
    [InlineData("""addROD("hosts_trafstat", {broken});""")]
    [InlineData("<html>Unsupported firmware response</html>")]
    public async Task InvalidOrMissingMeasurementsAreErrorsNotZeroes(string body)
    {
        using var handler = new ScriptedHandler(body);
        using var client = new GatewayClient(new HttpClient(handler) { BaseAddress = new("https://gateway.test") });
        await Assert.ThrowsAsync<GatewayDataException>(() => client.ReadCountersAsync(default));
    }

    [Fact]
    public async Task LoginPageReturnedWithSuccessStatusRequiresAuthentication()
    {
        using var handler = new ScriptedHandler("""window.location = "/login.cgi";""");
        using var client = new GatewayClient(new HttpClient(handler) { BaseAddress = new("https://gateway.test") });
        await Assert.ThrowsAsync<GatewayAuthenticationException>(() => client.ReadCountersAsync(default));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("""{"loginToken":7}""")]
    [InlineData("""{"loginToken":"bad"}""")]
    public async Task InvalidChallengeDoesNotSubmitAPassword(string challenge)
    {
        using var handler = new ScriptedHandler(challenge);
        using var client = new GatewayClient(new HttpClient(handler) { BaseAddress = new("https://gateway.test") });
        await Assert.ThrowsAsync<GatewayDataException>(() => client.LoginAsync("test-password", default));
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData("http://gateway.test")]
    [InlineData("https://admin:password@gateway.test")]
    [InlineData("https://gateway.test/path")]
    [InlineData("https://gateway.test/?token=secret")]
    public void ProductionClientRejectsInsecureOrAmbiguousOrigins(string endpoint)
    {
        Assert.Throws<ArgumentException>(() => GatewayClient.Create(new(endpoint)));
    }

    internal sealed class ScriptedHandler(params string[] replies) : HttpMessageHandler
    {
        private readonly Queue<string> replies = new(replies);
        public List<string> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Requests.Add(request.RequestUri!.AbsolutePath + " " +
                (request.Content is null ? "" : await request.Content.ReadAsStringAsync(token)));
            return new(HttpStatusCode.OK) { Content = new StringContent(replies.Dequeue(), Encoding.UTF8) };
        }
    }
}
