namespace Barkfluff.WebServer.Services;

public class VersionStore
{
    private readonly object _lock = new();

    private string _androidRelease = string.Empty;
    private string _androidBeta = string.Empty;
    private string _windowsRelease = string.Empty;
    private string _windowsBeta = string.Empty;
    private string _macosRelease = string.Empty;
    private string _macosBeta = string.Empty;

    public void SetAndroidRelease(string v) { lock (_lock) _androidRelease = v; }
    public void SetAndroidBeta(string v) { lock (_lock) _androidBeta = v; }
    public void SetWindowsRelease(string v) { lock (_lock) _windowsRelease = v; }
    public void SetWindowsBeta(string v) { lock (_lock) _windowsBeta = v; }
    public void SetMacosRelease(string v) { lock (_lock) _macosRelease = v; }
    public void SetMacosBeta(string v) { lock (_lock) _macosBeta = v; }

    public (string androidRelease, string androidBeta, string windowsRelease, string windowsBeta, string macosRelease, string macosBeta) GetAll()
    {
        lock (_lock)
            return (_androidRelease, _androidBeta, _windowsRelease, _windowsBeta, _macosRelease, _macosBeta);
    }
}
