namespace Barkfluff.WebServer.Services;

public class VersionStore
{
    private readonly object _lock = new();

    private string _androidRelease = string.Empty;
    private string _androidBeta = string.Empty;
    private string _androidDev = string.Empty;
    private string _androidNightly = string.Empty;
    private string _windowsRelease = string.Empty;
    private string _windowsBeta = string.Empty;
    private string _windowsDev = string.Empty;
    private string _windowsNightly = string.Empty;
    private string _macosRelease = string.Empty;
    private string _macosBeta = string.Empty;
    private string _macosDev = string.Empty;
    private string _macosNightly = string.Empty;

    public void SetAndroidRelease(string v) { lock (_lock) _androidRelease = v; }
    public void SetAndroidBeta(string v) { lock (_lock) _androidBeta = v; }
    public void SetAndroidDev(string v) { lock (_lock) _androidDev = v; }
    public void SetAndroidNightly(string v) { lock (_lock) _androidNightly = v; }
    public void SetWindowsRelease(string v) { lock (_lock) _windowsRelease = v; }
    public void SetWindowsBeta(string v) { lock (_lock) _windowsBeta = v; }
    public void SetWindowsDev(string v) { lock (_lock) _windowsDev = v; }
    public void SetWindowsNightly(string v) { lock (_lock) _windowsNightly = v; }
    public void SetMacosRelease(string v) { lock (_lock) _macosRelease = v; }
    public void SetMacosBeta(string v) { lock (_lock) _macosBeta = v; }
    public void SetMacosDev(string v) { lock (_lock) _macosDev = v; }
    public void SetMacosNightly(string v) { lock (_lock) _macosNightly = v; }

    public (
        string androidRelease, string androidBeta, string androidDev, string androidNightly,
        string windowsRelease, string windowsBeta, string windowsDev, string windowsNightly,
        string macosRelease, string macosBeta, string macosDev, string macosNightly) GetAll()
    {
        lock (_lock)
            return (
                _androidRelease, _androidBeta, _androidDev, _androidNightly,
                _windowsRelease, _windowsBeta, _windowsDev, _windowsNightly,
                _macosRelease, _macosBeta, _macosDev, _macosNightly);
    }
}
