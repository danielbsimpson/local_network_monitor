using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using LocalNetworkMonitor.Core;

namespace LocalNetworkMonitor.App;

public partial class MainWindow : Window
{
    private readonly WindowsCredentialStore store = new();
    private readonly CredentialSession credentials;
    private readonly Func<Uri, GatewayClient> createClient;
    private MeasurementHistory history = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DateTimeOffset clockOrigin = DateTimeOffset.UtcNow;
    private readonly long clockStart = Stopwatch.GetTimestamp();
    private CancellationTokenSource? monitoring;
    private Uri? historyOrigin;
    private DateTimeOffset? lastContact;
    private bool reachable;
    private string? selectedEntry;
    private bool refreshing;
    private DateTimeOffset Now => clockOrigin + Stopwatch.GetElapsedTime(clockStart);

    public MainWindow() : this(GatewayClient.Create) { }

    internal MainWindow(Func<Uri, GatewayClient> createClient)
    {
        this.createClient = createClient;
        InitializeComponent();
        credentials = new(store);
        timer.Tick += (_, _) => RefreshDisplay();
        timer.Start();
        Closed += (_, _) =>
        {
            timer.Stop();
            monitoring?.Cancel();
            Password.Clear();
            credentials.Dispose();
        };
        RefreshDisplay();
    }

    private Uri Endpoint()
    {
        if (!Uri.TryCreate(Gateway.Text.Trim(), UriKind.Absolute, out var endpoint))
            throw new ArgumentException("Enter a valid HTTPS gateway origin.");
        return GatewayClient.ValidateEndpoint(endpoint);
    }

    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        if (monitoring is not null) return;
        using var cancellation = new CancellationTokenSource();
        monitoring = cancellation;
        SetRunning(true);
        try
        {
            var endpoint = Endpoint();
            if (historyOrigin != endpoint)
            {
                history = new();
                historyOrigin = endpoint;
                lastContact = null;
                reachable = false;
                selectedEntry = null;
                RefreshDisplay();
            }
            if (Password.Password.Length == 0)
                throw new ArgumentException("Enter the router password or explicitly load a saved password.");
            using var client = createClient(endpoint);
            SetStatus("Connecting with normal HTTPS certificate validation...");
            await client.LoginAsync(Password.Password, cancellation.Token);
            var counters = await client.ReadCountersAsync(cancellation.Token);
            credentials.Accept(endpoint.GetLeftPart(UriPartial.Authority), Password.Password, Remember.IsChecked == true);
            Password.Clear();
            var session = new GatewaySession(client, credentials.Password!);
            Record(counters);
            SetStatus("Monitoring. Awaiting source counter changes; repeated replies are not fresh measurements.");
            var delay = TimeSpan.FromSeconds(5);
            while (!cancellation.IsCancellationRequested)
            {
                await Task.Delay(delay, cancellation.Token);
                try
                {
                    counters = await session.ReadAsync(cancellation.Token);
                    if (session.RenewedOnLastRead) history.BreakContinuity();
                    Record(counters);
                    delay = TimeSpan.FromSeconds(5);
                    SetStatus("Monitoring. Awaiting source counter changes; repeated replies are not fresh measurements.");
                }
                catch (GatewayAuthenticationException)
                {
                    throw;
                }
                catch (Exception error) when (error is HttpRequestException or GatewayDataException ||
                                              error is OperationCanceledException && !cancellation.IsCancellationRequested)
                {
                    history.BreakContinuity();
                    reachable = false;
                    SetStatus($"{Describe(error)} Next check in {Math.Min(60, delay.TotalSeconds * 2):0}s.", true);
                    delay = TimeSpan.FromSeconds(Math.Min(60, delay.TotalSeconds * 2));
                    RefreshDisplay();
                }
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            SetStatus("Disconnected. History remains in memory until it expires or the app closes.");
        }
        catch (Exception error) when (error is HttpRequestException or GatewayDataException or
                                      GatewayAuthenticationException or ArgumentException or
                                      Win32Exception or OperationCanceledException)
        {
            SetStatus(Describe(error), true);
        }
        finally
        {
            history.BreakContinuity();
            reachable = false;
            Password.Clear();
            credentials.Dispose();
            monitoring = null;
            SetRunning(false);
            RefreshDisplay();
        }
    }

    private void Record(IReadOnlyList<DeviceCounters> counters)
    {
        lastContact = Now;
        reachable = true;
        history.Observe(lastContact.Value, counters);
        RefreshDisplay();
    }

    private void SetRunning(bool running)
    {
        Connect.IsEnabled = Gateway.IsEnabled = Password.IsEnabled = Remember.IsEnabled = LoadSaved.IsEnabled = !running;
        Disconnect.IsEnabled = running;
    }

    private void Disconnect_Click(object sender, RoutedEventArgs e)
    {
        monitoring?.Cancel();
        Disconnect.IsEnabled = false;
    }

    private void LoadSaved_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var saved = store.Read(Endpoint().GetLeftPart(UriPartial.Authority));
            if (saved is null)
            {
                SetStatus("No password is saved for this exact gateway origin.");
                return;
            }
            Password.Password = saved;
            SetStatus("Saved password loaded for this connection. It will not be overwritten unless Remember is checked.");
        }
        catch (Exception error) when (error is ArgumentException or Win32Exception)
        {
            SetStatus(Describe(error), true);
        }
    }

    private void Forget_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            credentials.Forget(Endpoint().GetLeftPart(UriPartial.Authority));
            Password.Clear();
            SetStatus("Saved password removed. Any active in-memory session continues until disconnect or close.");
        }
        catch (Exception error) when (error is ArgumentException or Win32Exception)
        {
            SetStatus(Describe(error), true);
        }
    }

    private static string Describe(Exception error) => error switch
    {
        HttpRequestException { HttpRequestError: HttpRequestError.SecureConnectionError } =>
            "HTTPS trust/handshake failed. Login/collection blocked; verify the gateway hostname and trusted certificate. No bypass is enabled.",
        HttpRequestException => "Gateway request failed. Check gateway connectivity and HTTPS configuration; no fresh measurement is available.",
        OperationCanceledException => "Gateway request timed out. No fresh measurement is available.",
        _ => error.Message
    };

    private void SetStatus(string text, bool error = false)
    {
        Status.Text = text;
        Status.Foreground = error ? Brushes.DarkRed : Brushes.DarkSlateGray;
    }

    private void RefreshDisplay()
    {
        var now = Now;
        history.Prune(now);
        Reachability.Text = lastContact is { } contact
            ? $"{(reachable ? "Router reachable at last check" : "Router not currently confirmed reachable")} | last valid counter response {Age(now - contact)} ago."
            : "No valid counter response yet.";
        refreshing = true;
        try
        {
            Devices.ItemsSource = history.Entries.Select(pair =>
            {
                var entry = pair.Value;
                var last = entry.Intervals.LastOrDefault();
                var age = now - (entry.LastChange ?? entry.FirstObserved);
                return new DeviceRow(pair.Key, entry.Present ? "Reported" : "Not measured",
                    last is null ? "Unknown" : Rate(last.DownloadBytesPerSecond),
                    last is null ? "Unknown" : Rate(last.UploadBytesPerSecond),
                    last is null ? "Awaiting interval" :
                        $"{(last.End - last.Start).TotalSeconds:0.#}s average; ended {Age(now - last.End)} ago",
                    age.TotalSeconds >= 60
                        ? $"No counter change observed for {Age(age)}; traffic may be idle."
                        : entry.LastChange is null ? "Awaiting first counter change" :
                            $"Last observed change {Age(age)} ago; awaiting update");
            }).OrderBy(r => r.Id).ToArray();
            Devices.SelectedItem = Devices.Items.Cast<DeviceRow>().FirstOrDefault(r => r.Id == selectedEntry);
        }
        finally { refreshing = false; }
        UpdateChart();
    }

    private void Devices_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (refreshing) return;
        selectedEntry = (Devices.SelectedItem as DeviceRow)?.Id;
        UpdateChart();
    }

    private void UpdateChart()
    {
        Chart.Now = Now;
        Chart.Intervals = selectedEntry is not null && history.Entries.TryGetValue(selectedEntry, out var entry)
            ? entry.Intervals : [];
        ChartTitle.Text = selectedEntry is null
            ? "Select a device entry. Ten-minute session history; blanks mean unknown, not zero."
            : $"{selectedEntry} | ten-minute history. All displayed estimates are historical intervals.";
        Chart.InvalidateVisual();
    }

    private static string Rate(double bytesPerSecond) => $"{bytesPerSecond * 8 / 1_000_000:0.###} Mbps";
    private static string Age(TimeSpan age) => $"{Math.Max(0, age.TotalSeconds):0}s";
    private sealed record DeviceRow(string Id, string Coverage, string Download, string Upload, string Interval, string Change);
}
