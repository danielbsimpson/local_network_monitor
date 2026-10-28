using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LocalNetworkMonitor.Core;

public sealed class GatewayAuthenticationException(string message) : Exception(message);
public sealed class GatewayDataException(string message) : Exception(message);

public sealed class GatewayClient(HttpClient http) : IDisposable
{
    public static Uri ValidateEndpoint(Uri endpoint)
    {
        if (endpoint.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(endpoint.UserInfo) ||
            endpoint.AbsolutePath != "/" || !string.IsNullOrEmpty(endpoint.Query) ||
            !string.IsNullOrEmpty(endpoint.Fragment))
            throw new ArgumentException("Enter an HTTPS gateway origin without credentials, path, query, or fragment.");
        return endpoint;
    }

    public static GatewayClient Create(Uri endpoint)
    {
        ValidateEndpoint(endpoint);
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            CookieContainer = new CookieContainer(),
            UseProxy = false
        };
        var client = new HttpClient(handler) { BaseAddress = endpoint, Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.Referrer = endpoint;
        client.DefaultRequestHeaders.Add("Origin", endpoint.GetLeftPart(UriPartial.Authority));
        return new(client);
    }

    public async Task LoginAsync(string password, CancellationToken token)
    {
        using var status = await http.GetAsync("/loginStatus.cgi", token);
        status.EnsureSuccessStatusCode();
        using var document = ParseJson(await status.Content.ReadAsStringAsync(token));
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("loginToken", out var value) ||
            value.ValueKind != JsonValueKind.String ||
            value.GetString() is not { Length: 32 } challenge || !challenge.All(Uri.IsHexDigit))
            throw new GatewayDataException("Gateway returned an invalid login challenge.");
        challenge = challenge.ToLowerInvariant();
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["luci_username"] = HashIdentity("admin"),
            ["luci_password"] = Hex(SHA512.HashData(Encoding.ASCII.GetBytes(challenge + HashIdentity(password)))),
            ["luci_token"] = challenge,
            ["luci_view"] = "Mobile",
            ["luci_keep_login"] = "0"
        });
        using var response = await http.PostAsync("/login.cgi", form, token);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new GatewayAuthenticationException("Gateway rejected authentication. Re-enter the router password.");
        if ((int)response.StatusCode is not (>= 200 and < 400))
            response.EnsureSuccessStatusCode();
        if (!response.Headers.TryGetValues("Set-Cookie", out var cookies) ||
            !cookies.Any(c => Regex.IsMatch(c, @"(?:^|,\s*)sysauth=[^;\s,]+",
                RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))))
            throw new GatewayAuthenticationException("Gateway did not establish a session. Check credentials and firmware compatibility.");
    }

    public async Task<IReadOnlyList<DeviceCounters>> ReadCountersAsync(CancellationToken token)
    {
        using var response = await http.GetAsync("/cgi/cgi_bandwith.js", token);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden ||
            (int)response.StatusCode is >= 300 and < 400)
            throw new GatewayAuthenticationException("Gateway session expired.");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(token);
        if (body.Contains("login.cgi", StringComparison.OrdinalIgnoreCase) &&
            !body.Contains("hosts_trafstat", StringComparison.Ordinal))
            throw new GatewayAuthenticationException("Gateway requires authentication.");
        var match = Regex.Match(body, """addROD\(\s*["']hosts_trafstat["']\s*,\s*""",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        if (!match.Success)
            throw new GatewayDataException("Gateway response contains no hosts_trafstat measurement table.");
        try
        {
            var bytes = Encoding.UTF8.GetBytes(body[(match.Index + match.Length)..]);
            var reader = new Utf8JsonReader(bytes);
            using var document = JsonDocument.ParseValue(ref reader);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("3600", out var hosts) || hosts.ValueKind != JsonValueKind.Object)
                throw new GatewayDataException("Gateway response has no one-hour device counter table.");
            var entries = new List<DeviceCounters>();
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var host in hosts.EnumerateObject())
            {
                if (!Regex.IsMatch(host.Name, @"\A(?:[0-9a-fA-F]{2}:){5}[0-9a-fA-F]{2}\z",
                        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)) || !ids.Add(host.Name) ||
                    host.Value.ValueKind != JsonValueKind.Object ||
                    !host.Value.TryGetProperty("bytes_rx", out var rx) ||
                    !host.Value.TryGetProperty("bytes_tx", out var tx) ||
                    rx.ValueKind != JsonValueKind.Number || tx.ValueKind != JsonValueKind.Number ||
                    !rx.TryGetUInt64(out var download) || !tx.TryGetUInt64(out var upload))
                    throw new GatewayDataException("Gateway returned invalid or duplicate device counters; no rates were updated.");
                entries.Add(new(host.Name, download, upload));
            }
            return entries;
        }
        catch (JsonException)
        {
            throw new GatewayDataException("Gateway measurement table is not supported JSON; no rates were updated.");
        }
    }

    private static JsonDocument ParseJson(string text)
    {
        try { return JsonDocument.Parse(text); }
        catch (JsonException) { throw new GatewayDataException("Gateway returned an invalid authentication response."); }
    }

    private static string HashIdentity(string text)
    {
        // The gateway protocol hashes the low eight bits of each UTF-16 code unit.
        var bytes = text.Select(c => (byte)c).ToArray();
        try
        {
            return Hex(SHA512.HashData(Encoding.ASCII.GetBytes(Hex(MD5.HashData(bytes)))));
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();
    public void Dispose() => http.Dispose();
}
