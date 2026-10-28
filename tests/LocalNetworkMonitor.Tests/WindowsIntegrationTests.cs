using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using LocalNetworkMonitor.App;
using LocalNetworkMonitor.Core;
using Xunit;

namespace LocalNetworkMonitor.Tests;

public class WindowsIntegrationTests
{
    [Fact]
    public void CredentialManagerRoundTripsAndDeletesAnIsolatedSyntheticCredential()
    {
        var store = new WindowsCredentialStore();
        var gateway = $"https://lnm-test-{Guid.NewGuid():N}.invalid";
        try
        {
            Assert.Null(store.Read(gateway));
            store.Write(gateway, "synthetic-test-password");
            Assert.Equal("synthetic-test-password", store.Read(gateway));
            store.Delete(gateway);
            Assert.Null(store.Read(gateway));
        }
        finally { store.Delete(gateway); }
    }

    [Fact]
    public void DashboardLoadsAndKeepsUnverifiedControlsDisabled()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            MainWindow? window = null;
            try
            {
                window = new MainWindow();
                window.Show();
                window.UpdateLayout();
                Assert.True(window.IsVisible);
                Assert.False(((CheckBox)window.FindName("Remember")).IsChecked);
                Assert.True(((Button)window.FindName("Connect")).IsEnabled);
                Assert.False(((Button)window.FindName("Disconnect")).IsEnabled);
                Assert.False(((Button)window.FindName("PauseInternet")).IsEnabled);
                Assert.False(((Button)window.FindName("ResumeInternet")).IsEnabled);
                Assert.True(window.ActualWidth >= 960);
            }
            catch (Exception error) { failure = error; }
            finally { window?.Close(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "Dashboard did not initialize responsively.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [Theory]
    [InlineData("https://second-gateway.test", false, false)]
    [InlineData("https://first-gateway.test:8443", false, false)]
    [InlineData("https://second-gateway.test", true, false)]
    [InlineData("https://first-gateway.test", false, true)]
    [InlineData("https://FIRST-GATEWAY.test:443", false, true)]
    [InlineData("https://first-gateway.test", true, true)]
    public void ReconnectingRetainsHistoryOnlyForTheSameGatewayOrigin(
        string nextGateway, bool rejectLogin, bool retainHistory)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            MainWindow? window = null;
            try
            {
                var connections = 0;
                window = new MainWindow(endpoint =>
                {
                    var firstConnection = ++connections == 1;
                    return new GatewayClient(new HttpClient(
                        new LoginHandler(firstConnection, !firstConnection && rejectLogin))
                    { BaseAddress = endpoint });
                });
                window.Show();
                var gateway = (TextBox)window.FindName("Gateway");
                var password = (PasswordBox)window.FindName("Password");
                var connect = (Button)window.FindName("Connect");
                var disconnect = (Button)window.FindName("Disconnect");
                var devices = (DataGrid)window.FindName("Devices");
                var title = (TextBlock)window.FindName("ChartTitle");
                var contact = (TextBlock)window.FindName("Reachability");

                gateway.Text = "https://first-gateway.test";
                password.Password = "synthetic-test-password";
                connect.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(2, devices.Items.Count);
                devices.SelectedIndex = 0;
                Assert.StartsWith("02:00:00:00:00:01 |", title.Text);
                disconnect.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                WaitForDisconnect(connect);

                gateway.Text = nextGateway;
                password.Password = "synthetic-test-password";
                connect.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                Assert.Equal(retainHistory ? 2 : rejectLogin ? 0 : 1, devices.Items.Count);
                Assert.Equal(retainHistory, devices.SelectedItem is not null);
                if (!retainHistory)
                {
                    Assert.StartsWith("Select a device entry.", title.Text);
                    if (rejectLogin) Assert.Equal("No valid counter response yet.", contact.Text);
                }
                Assert.Equal(2, connections);
                disconnect.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                WaitForDisconnect(connect);
            }
            catch (Exception error) { failure = error; }
            finally { window?.Close(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "Gateway reconnect test did not finish responsively.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void WaitForDisconnect(Button connect)
    {
        var frame = new DispatcherFrame();
        var elapsed = Stopwatch.StartNew();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (_, _) =>
        {
            if (connect.IsEnabled || elapsed.Elapsed > TimeSpan.FromSeconds(5))
                frame.Continue = false;
        };
        timer.Start();
        try { Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
        Assert.True(connect.IsEnabled, "Monitoring did not finish disconnecting.");
    }

    private sealed class LoginHandler(bool firstConnection, bool rejectLogin) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.RequestUri!.AbsolutePath == "/loginStatus.cgi")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"loginToken":"0123456789abcdef0123456789abcdef"}""")
                });
            if (request.RequestUri.AbsolutePath == "/login.cgi")
            {
                var response = new HttpResponseMessage(rejectLogin ? HttpStatusCode.Forbidden : HttpStatusCode.Found);
                if (!rejectLogin) response.Headers.Add("Set-Cookie", "sysauth=synthetic-session; Path=/; HttpOnly");
                return Task.FromResult(response);
            }
            Assert.Equal("/cgi/cgi_bandwith.js", request.RequestUri.AbsolutePath);
            var entries = firstConnection
                ? """
                  "02:00:00:00:00:01":{"bytes_rx":100,"bytes_tx":200},
                  "02:00:00:00:00:02":{"bytes_rx":300,"bytes_tx":400}
                  """
                : "\"02:00:00:00:00:02\":{\"bytes_rx\":500,\"bytes_tx\":600}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("addROD(\"hosts_trafstat\", {\"3600\":{" + entries + "}});")
            });
        }
    }
}
