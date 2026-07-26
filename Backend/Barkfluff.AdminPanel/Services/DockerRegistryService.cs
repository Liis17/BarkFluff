using Barkfluff.AdminPanel.Models.Dtos;

using System.Text.Json;
using System.Text.RegularExpressions;

namespace Barkfluff.AdminPanel.Services;

public class DockerRegistryService
{
    private const string RegistryPrefix = "docker.barkfluff.com:5000/";
    private static readonly Regex SemverTagRegex = new(
        "^(?<major>0|[1-9]\\d*)\\.(?<minor>0|[1-9]\\d*)\\.(?<patch>0|[1-9]\\d*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly HttpClient _httpClient;
    private readonly ILogger<DockerRegistryService> _logger;

    public DockerRegistryService(HttpClient httpClient, ILogger<DockerRegistryService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<ImageVersionStatusDto> GetVersionStatusAsync(string image, CancellationToken cancellationToken = default)
    {
        if (!TryParseImageReference(image, out var repository, out var currentVersion))
            return new ImageVersionStatusDto();

        try
        {
            using var response = await _httpClient.GetAsync($"/v2/{repository}/tags/list", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug("Docker registry returned {StatusCode} for image repository {Repository}", response.StatusCode, repository);
                return new ImageVersionStatusDto();
            }

            await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken);
            if (!document.RootElement.TryGetProperty("tags", out var tags) || tags.ValueKind != JsonValueKind.Array)
                return new ImageVersionStatusDto();

            Version? latest = null;
            string? latestVersion = null;
            foreach (var tag in tags.EnumerateArray())
            {
                if (tag.ValueKind != JsonValueKind.String || !TryParseSemver(tag.GetString(), out var version))
                    continue;

                if (latest is null || version.CompareTo(latest) > 0)
                {
                    latest = version;
                    latestVersion = tag.GetString();
                }
            }

            if (latest is null)
                return new ImageVersionStatusDto();

            return new ImageVersionStatusDto
            {
                CurrentVersion = currentVersion,
                LatestVersion = latestVersion,
                UpdateAvailable = latest is not null && Version.Parse(currentVersion).CompareTo(latest) < 0
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            _logger.LogDebug(ex, "Could not check Docker registry versions for image repository {Repository}", repository);
            return new ImageVersionStatusDto();
        }
    }

    private static bool TryParseImageReference(string image, out string repository, out string currentVersion)
    {
        repository = string.Empty;
        currentVersion = string.Empty;

        if (!image.StartsWith(RegistryPrefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var imageReference = image[RegistryPrefix.Length..];
        var tagSeparatorIndex = imageReference.LastIndexOf(':');
        if (tagSeparatorIndex <= imageReference.LastIndexOf('/') || tagSeparatorIndex == imageReference.Length - 1)
            return false;

        repository = imageReference[..tagSeparatorIndex];
        var tag = imageReference[(tagSeparatorIndex + 1)..];
        if (!repository.StartsWith("barkfluff-", StringComparison.OrdinalIgnoreCase) || !TryParseSemver(tag, out _))
            return false;

        currentVersion = tag;
        return true;
    }

    private static bool TryParseSemver(string? tag, out Version version)
    {
        version = new Version();
        if (tag is null)
            return false;

        var match = SemverTagRegex.Match(tag);
        if (!match.Success)
            return false;

        return int.TryParse(match.Groups["major"].Value, out var major) &&
               int.TryParse(match.Groups["minor"].Value, out var minor) &&
               int.TryParse(match.Groups["patch"].Value, out var patch) &&
               (version = new Version(major, minor, patch)) is not null;
    }
}
