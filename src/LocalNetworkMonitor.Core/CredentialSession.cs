namespace LocalNetworkMonitor.Core;

public interface ICredentialStore
{
    string? Read(string gateway);
    void Write(string gateway, string password);
    void Delete(string gateway);
}

public sealed class CredentialSession(ICredentialStore store) : IDisposable
{
    public string? Password { get; private set; }

    public void Accept(string gateway, string password, bool remember)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        if (remember)
            store.Write(gateway, password);
        Password = password;
    }

    public void Forget(string gateway) => store.Delete(gateway);
    public void Dispose() => Password = null;
}
