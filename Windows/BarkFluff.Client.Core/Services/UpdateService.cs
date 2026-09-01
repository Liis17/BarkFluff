using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Serialization;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace BarkFluff.Client.Core.Services;

public sealed class UpdateService : IUpdateService
{
    private const string BaseStorageUrl = "https://storage.barkfluff.com/get/barkfluffwinui";

    private readonly HttpClient _httpClient;
    private readonly string? _currentIdentityName;
    private readonly Version _currentVersion;

    public UpdateService(HttpClient httpClient)
        : this(httpClient, GetCurrentPackageIdentityName(), GetCurrentAssemblyVersion())
    {
    }

    internal UpdateService(HttpClient httpClient, string? currentIdentityName, Version? currentVersion = null)
    {
        _httpClient = httpClient;
        _currentIdentityName = currentIdentityName;
        _currentVersion = UpdateVersion.Normalize(currentVersion) ?? new Version(0, 0, 0);
        CurrentChannel = UpdateChannelResolver.Resolve(currentIdentityName);
    }

    public UpdateChannel? CurrentChannel { get; }

    public async Task<UpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        if (CurrentChannel is not { } channel)
        {
            return new UpdateCheckResult(null, false);
        }

        var response = await _httpClient.GetFromJsonAsync<VersionResponse>(
            GetVersionUrl(channel), cancellationToken);
        var latestVersion = UpdateVersion.Normalize(response?.Version);
        var latestVersionText = UpdateVersion.Format(latestVersion);
        var hasUpdate = latestVersion is not null && latestVersion > _currentVersion;

        return new UpdateCheckResult(latestVersionText, hasUpdate);
    }

    public async Task<string?> DownloadUpdateAsync(CancellationToken cancellationToken = default)
    {
        if (CurrentChannel is not { } channel || string.IsNullOrWhiteSpace(_currentIdentityName))
        {
            return null;
        }

        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "BarkFluff",
            "Updates",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);

        var archivePath = Path.Combine(temporaryDirectory, "update.zip");
        try
        {
            using var response = await _httpClient.GetAsync(
                GetDownloadUrl(channel),
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            await using (var archiveStream = new FileStream(
                archivePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                options: FileOptions.Asynchronous))
            {
                await response.Content.CopyToAsync(archiveStream, cancellationToken);
            }

            ZipFile.ExtractToDirectory(archivePath, temporaryDirectory, overwriteFiles: false);
            var packagePath = FindMatchingPackage(temporaryDirectory);
            if (packagePath is null)
            {
                throw new InvalidDataException(
                    $"Архив обновления не содержит MSIX для identity '{_currentIdentityName}' новее текущей версии.");
            }

            File.Delete(archivePath);
            return packagePath;
        }
        catch
        {
            DeleteTemporaryDirectory(temporaryDirectory);
            throw;
        }
    }

    internal static string GetVersionUrl(UpdateChannel channel) =>
        $"{BaseStorageUrl}/{UpdateChannelResolver.GetRouteSegment(channel)}/version";

    internal static string GetDownloadUrl(UpdateChannel channel) =>
        $"{BaseStorageUrl}/{UpdateChannelResolver.GetRouteSegment(channel)}";

    private string? FindMatchingPackage(string temporaryDirectory)
    {
        var packages = new List<(string Path, Version Version)>();
        foreach (var candidatePath in Directory.EnumerateFiles(
                     temporaryDirectory,
                     "*.msix",
                     SearchOption.AllDirectories))
        {
            try
            {
                using var package = ZipFile.OpenRead(candidatePath);
                var manifestEntry = package.GetEntry("AppxManifest.xml");
                if (manifestEntry is null)
                {
                    continue;
                }

                using var manifestStream = manifestEntry.Open();
                using var xmlReader = XmlReader.Create(manifestStream, new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null
                });
                var manifest = XDocument.Load(xmlReader);
                var identity = manifest.Root?
                    .Elements()
                    .FirstOrDefault(element => element.Name.LocalName == "Identity");
                if (identity is null ||
                    !string.Equals(
                        (string?)identity.Attribute("Name"),
                        _currentIdentityName,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                var packageVersion = UpdateVersion.Normalize((string?)identity.Attribute("Version"));
                if (packageVersion is not null && packageVersion > _currentVersion)
                {
                    packages.Add((candidatePath, packageVersion));
                }
            }
            catch (InvalidDataException)
            {
                // Other files in the archive are not necessarily valid MSIX packages.
            }
            catch (XmlException)
            {
                // Ignore malformed dependency/package manifests and keep looking for the main package.
            }
        }

        return packages
            .OrderByDescending(package => package.Version)
            .Select(package => package.Path)
            .FirstOrDefault();
    }

    private static void DeleteTemporaryDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string? GetCurrentPackageIdentityName()
    {
        try
        {
            return Windows.ApplicationModel.Package.Current.Id.Name;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static Version? GetCurrentAssemblyVersion() =>
        Assembly.GetEntryAssembly()?.GetName().Version;

    private sealed record VersionResponse([property: JsonPropertyName("version")] string? Version);
}
