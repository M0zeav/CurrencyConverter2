namespace CurrencyConverter2.Services;

public sealed class ServerSettings
{
    private const string Key = "server-address";
    private static string DefaultAddress => DeviceInfo.Platform == DevicePlatform.Android
        ? "http://10.0.2.2:5080/" : "http://localhost:5080/";
    public string Address => Preferences.Default.Get(Key, DefaultAddress);
    public bool TrySave(string? address)
    {
        if (!Uri.TryCreate(address?.Trim(), UriKind.Absolute, out var uri)) return false;
        var local = uri.IsLoopback || uri.Host == "10.0.2.2";
        if (uri.Scheme != "https" && !(uri.Scheme == "http" && local)) return false;
        if (!string.IsNullOrEmpty(uri.UserInfo + uri.Query + uri.Fragment)) return false;
        Preferences.Default.Set(Key, uri.AbsoluteUri.TrimEnd('/') + "/");
        return true;
    }
}
