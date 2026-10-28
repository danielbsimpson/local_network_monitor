using LocalNetworkMonitor.Core;
using Xunit;

namespace LocalNetworkMonitor.Tests;

public class CredentialSessionTests
{
    [Fact]
    public void RememberingIsOptInAndForgettingDoesNotEndActiveSession()
    {
        var store = new MemoryStore();
        using var session = new CredentialSession(store);
        session.Accept("https://gateway.test", "session-only", remember: false);
        Assert.Null(store.Read("https://gateway.test"));
        Assert.Equal("session-only", session.Password);
        session.Accept("https://gateway.test", "remembered", remember: true);
        Assert.Equal("remembered", store.Read("https://gateway.test"));
        session.Forget("https://gateway.test");
        Assert.Null(store.Read("https://gateway.test"));
        Assert.Equal("remembered", session.Password);
        session.Dispose();
        Assert.Null(session.Password);
    }

    [Fact]
    public void SessionOnlyConnectionDoesNotOverwriteAnExistingSavedPassword()
    {
        var store = new MemoryStore();
        store.Write("https://gateway.test", "existing");
        using var session = new CredentialSession(store);
        session.Accept("https://gateway.test", "temporary", remember: false);
        Assert.Equal("existing", store.Read("https://gateway.test"));
        Assert.Equal("temporary", session.Password);
        Assert.Null(store.Read("https://different-gateway.test"));
    }

    private sealed class MemoryStore : ICredentialStore
    {
        private readonly Dictionary<string, string> passwords = [];
        public string? Read(string gateway) => passwords.GetValueOrDefault(gateway);
        public void Write(string gateway, string password) => passwords[gateway] = password;
        public void Delete(string gateway) => passwords.Remove(gateway);
    }
}
