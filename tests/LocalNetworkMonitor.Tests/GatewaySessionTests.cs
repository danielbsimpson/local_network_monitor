using System.Net;
using System.Net.Http;
using LocalNetworkMonitor.Core;
using Xunit;

namespace LocalNetworkMonitor.Tests;

public class GatewaySessionTests
{
    [Fact]
    public async Task RejectedRenewalStopsAutomaticAuthenticationRetries()
    {
        using var handler = new Responses(
            new(HttpStatusCode.Unauthorized),
            new(HttpStatusCode.OK) { Content = new StringContent("""{"loginToken":"0123456789abcdef0123456789abcdef"}""") },
            new(HttpStatusCode.Forbidden));
        using var client = new GatewayClient(new HttpClient(handler) { BaseAddress = new("https://gateway.test") });
        var session = new GatewaySession(client, "test-password");
        await Assert.ThrowsAsync<GatewayAuthenticationException>(() => session.ReadAsync(default));
        await Assert.ThrowsAsync<GatewayAuthenticationException>(() => session.ReadAsync(default));
        Assert.Equal(3, handler.RequestCount);
    }

    [Fact]
    public async Task ExpiredSessionRenewsOnceAndReturnsVerifiedCounters()
    {
        var login = new HttpResponseMessage(HttpStatusCode.Found);
        login.Headers.Add("Set-Cookie", "sysauth=synthetic-session; Path=/; HttpOnly");
        using var handler = new Responses(
            new(HttpStatusCode.Unauthorized),
            new(HttpStatusCode.OK) { Content = new StringContent("""{"loginToken":"0123456789abcdef0123456789abcdef"}""") },
            login,
            new(HttpStatusCode.OK) { Content = new StringContent("""addROD("hosts_trafstat", {"3600":{}});""") });
        using var client = new GatewayClient(new HttpClient(handler) { BaseAddress = new("https://gateway.test") });
        var session = new GatewaySession(client, "test-password");
        Assert.Empty(await session.ReadAsync(default));
        Assert.True(session.RenewedOnLastRead);
        Assert.Equal(4, handler.RequestCount);
    }

    [Fact]
    public async Task UnreachableGatewayDuringRenewalRequiresCredentialsWithoutPollingAgain()
    {
        using var handler = new Responses(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var client = new GatewayClient(new HttpClient(handler) { BaseAddress = new("https://gateway.test") });
        var session = new GatewaySession(client, "test-password");
        // There is no second HTTP response: the boundary fails while fetching the login challenge.
        await Assert.ThrowsAsync<GatewayAuthenticationException>(() => session.ReadAsync(default));
        await Assert.ThrowsAsync<GatewayAuthenticationException>(() => session.ReadAsync(default));
        Assert.Equal(2, handler.RequestCount);
    }

    private sealed class Responses(params HttpResponseMessage[] replies) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> replies = new(replies);
        public int RequestCount { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            RequestCount++;
            if (replies.Count == 0) throw new HttpRequestException("Synthetic network outage.");
            return Task.FromResult(replies.Dequeue());
        }
    }
}
