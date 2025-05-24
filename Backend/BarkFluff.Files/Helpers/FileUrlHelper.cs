namespace BarkFluff.Files.Helpers;

public static class FileUrlHelper
{
    public static string GenerateDownloadUrl(string host, int? port, Guid fileId)
    {
        if (port is null || port == 80 || port == 443 || port == 0)
        {
            return $"{host}/download/{fileId.ToString()}";
        }
        
        return $"{host}:{port}/download/{fileId.ToString()}";
    }
}