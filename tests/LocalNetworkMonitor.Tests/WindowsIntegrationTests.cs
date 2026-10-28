using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using LocalNetworkMonitor.App;
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
}
