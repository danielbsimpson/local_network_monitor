namespace LocalNetworkMonitor.Core;

public sealed class GatewaySession(GatewayClient client, string password)
{
    private bool authenticationBlocked;
    public bool RenewedOnLastRead { get; private set; }

    public async Task<IReadOnlyList<DeviceCounters>> ReadAsync(CancellationToken token)
    {
        RenewedOnLastRead = false;
        if (authenticationBlocked)
            throw new GatewayAuthenticationException("Automatic re-login stopped. Re-enter credentials and connect again.");
        try
        {
            return await client.ReadCountersAsync(token);
        }
        catch (GatewayAuthenticationException)
        {
            authenticationBlocked = true;
            try
            {
                await client.LoginAsync(password, token);
                var counters = await client.ReadCountersAsync(token);
                authenticationBlocked = false;
                RenewedOnLastRead = true;
                return counters;
            }
            catch (Exception error) when (error is HttpRequestException or GatewayDataException ||
                                          error is OperationCanceledException && !token.IsCancellationRequested)
            {
                var reason = error is HttpRequestException { HttpRequestError: HttpRequestError.SecureConnectionError }
                    ? "HTTPS certificate validation/handshake failed."
                    : "The gateway was unreachable or returned an incompatible response.";
                throw new GatewayAuthenticationException($"Automatic re-login failed. {reason} Retries stopped; reconnect explicitly.");
            }
        }
    }
}
