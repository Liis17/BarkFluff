using Barkfluff.AdminPanel.Models.Dtos;

using Microsoft.Extensions.Caching.Memory;

using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Barkfluff.AdminPanel.Services;

public class DockerRegistryService
{
    private const string RegistryPrefix = "docker.barkfluff.com:5000/";
    private static readonly SemaphoreSlim ManifestRequestGate = new(8, 8);
    private static readonly Regex SemverTagRegex = new(
        "^(?<major>0|[1-9]\\d*)\\.(?<minor>0|[1-9]\\d*)\\.(?<patch>0|[1-9]\\d*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;
    private readonly ILogger<DockerRegistryService> _logger;

    public DockerRegistryService(HttpClient httpClient, IMemoryCache cache, ILogger<DockerRegistryService> logger)
    {
        _httpClient = httpClient;
        _cache = cache;
        _logger = logger;
    }

    public async Task<ImageVersionStatusDto> GetVersionStatusAsync(
        string image,
        string? imageDigest = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseImageReference(image, out var repository, out var currentVersion))
            return new ImageVersionStatusDto();

        if (currentVersion is null && string.IsNullOrWhiteSpace(imageDigest))
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

            var versionTags = new List<(string tag, Version version)>();
            foreach (var tag in tags.EnumerateArray())
            {
                if (tag.ValueKind != JsonValueKind.String || !TryParseSemver(tag.GetString(), out var version))
                    continue;

                versionTags.Add((tag.GetString()!, version));
            }

            if (versionTags.Count == 0)
                return new ImageVersionStatusDto();

            var latest = versionTags.MaxBy(tag => tag.version);
            var installedVersion = currentVersion;
            Version? installedSemanticVersion = null;
            if (currentVersion is not null)
            {
                TryParseSemver(currentVersion, out installedSemanticVersion);
            }
            else if (!string.IsNullOrWhiteSpace(imageDigest))
            {
                var normalizedImageDigest = NormalizeDigest(imageDigest);
                var manifestDigests = await Task.WhenAll(versionTags.Select(async versionTag => new
                {
                    versionTag.tag,
                    versionTag.version,
                    digest = await GetManifestDigestAsync(repository, versionTag.tag, cancellationToken)
                }));
                var installedTag = manifestDigests.FirstOrDefault(tag =>
                    tag.digest is not null && string.Equals(tag.digest, normalizedImageDigest, StringComparison.OrdinalIgnoreCase));

                if (installedTag is null)
                    return new ImageVersionStatusDto();

                installedVersion = installedTag.tag;
                installedSemanticVersion = installedTag.version;
            }

            if (installedVersion is null || installedSemanticVersion is null)
                return new ImageVersionStatusDto();

            return new ImageVersionStatusDto
            {
                CurrentVersion = installedVersion,
                LatestVersion = latest.tag,
                UpdateAvailable = installedSemanticVersion.CompareTo(latest.version) < 0
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            _logger.LogDebug(ex, "Could not check Docker registry versions for image repository {Repository}", repository);
            return new ImageVersionStatusDto();
        }
    }

    private async Task<string?> GetManifestDigestAsync(string repository, string tag, CancellationToken cancellationToken)
    {
        var cacheKey = $"docker-registry:manifest:{repository}:{tag}";
        if (_cache.TryGetValue(cacheKey, out string? cachedDigest))
            return cachedDigest;

        await ManifestRequestGate.WaitAsync(cancellationToken);
        try
        {
            if (_cache.TryGetValue(cacheKey, out cachedDigest))
                return cachedDigest;

            using var request = new HttpRequestMessage(HttpMethod.Get, $"/v2/{repository}/manifests/{tag}");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.oci.image.index.v1+json"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.oci.image.manifest.v1+json"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.docker.distribution.manifest.list.v2+json"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.docker.distribution.manifest.v2+json"));

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;

            var digest = response.Headers.TryGetValues("Docker-Content-Digest", out var digests)
                ? digests.FirstOrDefault()
                : null;
            if (digest is not null)
                _cache.Set(cacheKey, digest, TimeSpan.FromMinutes(5));

            return digest;
        }
        finally
        {
            ManifestRequestGate.Release();
        }
    }

    private static bool TryParseImageReference(string image, out string repository, out string? currentVersion)
    {
        repository = string.Empty;
        currentVersion = null;

        if (!image.StartsWith(RegistryPrefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var imageReference = image[RegistryPrefix.Length..];
        var tagSeparatorIndex = imageReference.LastIndexOf(':');
        if (tagSeparatorIndex <= imageReference.LastIndexOf('/') || tagSeparatorIndex == imageReference.Length - 1)
            return false;

        repository = imageReference[..tagSeparatorIndex];
        var tag = imageReference[(tagSeparatorIndex + 1)..];
        if (!repository.StartsWith("barkfluff-", StringComparison.OrdinalIgnoreCase))
            return false;

        if (TryParseSemver(tag, out _))
        {
            currentVersion = tag;
            return true;
        }

        return string.Equals(tag, "latest", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeDigest(string imageDigest)
    {
        var separatorIndex = imageDigest.LastIndexOf('@');
        return separatorIndex >= 0 ? imageDigest[(separatorIndex + 1)..] : imageDigest;
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
