namespace Barkfluff.WebServer.Services;

public class VersionStore
{
    private readonly object _lock = new();

    private string _androidRelease = string.Empty;
    private string _androidBeta = string.Empty;
    private string _windowsRelease = string.Empty;
    private string _windowsBeta = string.Empty;

    public void SetAndroidRelease(string v) { lock (_lock) _androidRelease = v; }
    public void SetAndroidBeta(string v) { lock (_lock) _androidBeta = v; }
    public void SetWindowsRelease(string v) { lock (_lock) _windowsRelease = v; }
    public void SetWindowsBeta(string v) { lock (_lock) _windowsBeta = v; }

    public (string androidRelease, string androidBeta, string windowsRelease, string windowsBeta) GetAll()
    {
        lock (_lock)
            return (_androidRelease, _androidBeta, _windowsRelease, _windowsBeta);
    }
}
