using BarkFluff.Client.Core.Services;

using System.IO.Compression;
using System.Net;
using System.Text;

namespace BarkFluff.Client.WinUI.Tests;

public sealed class UpdateServiceTests
{
    [Theory]
    [InlineData("7895OrbitinSpace.Barkfluff", UpdateChannel.Release)]
    [InlineData("7895OrbitinSpace.Barkfluff.Dev", UpdateChannel.Dev)]
    [InlineData("7895OrbitinSpace.Barkfluff.Nightly", UpdateChannel.Nightly)]
    public void Identity_ResolvesToItsOwnChannel(string identityName, UpdateChannel expectedChannel)
    {
        var service = new UpdateService(new HttpClient(), identityName, new Version(1, 2, 3));

        Assert.Equal(expectedChannel, service.CurrentChannel);
    }

    [Fact]
    public void UnknownIdentity_DoesNotSelectAnUpdateChannel()
    {
        var service = new UpdateService(new HttpClient(), "7895OrbitinSpace.Barkfluff.Other", new Version(1, 2, 3));

        Assert.Null(service.CurrentChannel);
    }

    [Theory]
    [InlineData("7895OrbitinSpace.Barkfluff", "release")]
    [InlineData("7895OrbitinSpace.Barkfluff.Dev", "dev")]
    [InlineData("7895OrbitinSpace.Barkfluff.Nightly", "nightly")]
    public async Task Check_UsesOnlyTheCurrentChannel(string identityName, string expectedChannel)
    {
        Uri? requestedUri = null;
        using var httpClient = CreateHttpClient(request =>
        {
            requestedUri = request.RequestUri;
            return JsonResponse("{\"version\":\"2.3.4.0\"}");
        });
        var service = new UpdateService(httpClient, identityName, new Version(2, 3, 3, 0));

        var result = await service.CheckForUpdatesAsync();

        Assert.Equal(
            $"https://storage.barkfluff.com/get/barkfluffwinui/{expectedChannel}/version",
            requestedUri?.AbsoluteUri);
        Assert.Equal("2.3.4", result.LatestVersion);
        Assert.True(result.IsUpdateAvailable);
    }

    [Fact]
    public async Task Check_DoesNotTreatManifestRevisionAsAnUpdate()
    {
        using var httpClient = CreateHttpClient(_ => JsonResponse("{\"version\":\"2.3.3.0\"}"));
        var service = new UpdateService(
            httpClient,
            "7895OrbitinSpace.Barkfluff",
            new Version(2, 3, 3, 0));

        var result = await service.CheckForUpdatesAsync();

        Assert.Equal("2.3.3", result.LatestVersion);
        Assert.False(result.IsUpdateAvailable);
    }

    [Theory]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("1.2.3.0", "1.2.3")]
    public void Versions_AreComparedAfterNormalizingTheRevision(string input, string expected)
    {
        Assert.Equal(expected, UpdateVersion.Format(UpdateVersion.Normalize(input)));
    }

    [Theory]
    [InlineData("1.2", false)]
    [InlineData("1.2.x", false)]
    [InlineData("1.2.3.4.5", false)]
    public void InvalidVersions_AreRejected(string input, bool expected)
    {
        Assert.Equal(expected, UpdateVersion.Normalize(input) is not null);
    }

    [Fact]
    public async Task Download_SelectsTheMsixWithTheCurrentIdentity()
    {
        var archive = CreateArchive(
            ("foreign.msix", "7895OrbitinSpace.Barkfluff", "9.0.0.0"),
            ("nested/current.msix", "7895OrbitinSpace.Barkfluff.Dev", "1.2.4.0"));
        Uri? requestedUri = null;
        using var httpClient = CreateHttpClient(request =>
        {
            requestedUri = request.RequestUri;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(archive)
            };
        });
        var service = new UpdateService(
            httpClient,
            "7895OrbitinSpace.Barkfluff.Dev",
            new Version(1, 2, 3, 0));

        var packagePath = await service.DownloadUpdateAsync();

        Assert.Equal(
            "https://storage.barkfluff.com/get/barkfluffwinui/dev",
            requestedUri?.AbsoluteUri);
        Assert.NotNull(packagePath);
        try
        {
            Assert.EndsWith("current.msix", packagePath, StringComparison.OrdinalIgnoreCase);
            using var package = ZipFile.OpenRead(packagePath!);
            using var reader = new StreamReader(package.GetEntry("AppxManifest.xml")!.Open());
            var manifest = reader.ReadToEnd();
            Assert.Contains("7895OrbitinSpace.Barkfluff.Dev", manifest, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDownloadDirectory(packagePath!);
        }
    }

    [Fact]
    public async Task Download_RejectsAnArchiveFromAnotherChannel()
    {
        var archive = CreateArchive(
            ("foreign.msix", "7895OrbitinSpace.Barkfluff", "9.0.0.0"));
        using var httpClient = CreateHttpClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(archive)
        });
        var service = new UpdateService(
            httpClient,
            "7895OrbitinSpace.Barkfluff.Nightly",
            new Version(1, 2, 3, 0));

        await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadUpdateAsync());
    }

    private static HttpClient CreateHttpClient(Func<HttpRequestMessage, HttpResponseMessage> responder) =>
        new(new StubHttpMessageHandler(responder));

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private static byte[] CreateArchive(params (string Path, string Identity, string Version)[] packages)
    {
        using var archiveStream = new MemoryStream();
        using (var archive = new ZipArchive(archiveStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var package in packages)
            {
                var packageEntry = archive.CreateEntry(package.Path);
                using var packageStream = packageEntry.Open();
                var packageBytes = CreateMsix(package.Identity, package.Version);
                packageStream.Write(packageBytes);
            }
        }

        return archiveStream.ToArray();
    }

    private static byte[] CreateMsix(string identity, string version)
    {
        using var packageStream = new MemoryStream();
        using (var package = new ZipArchive(packageStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var manifestEntry = package.CreateEntry("AppxManifest.xml");
            using var writer = new StreamWriter(manifestEntry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            writer.Write($"<Package xmlns=\"http://schemas.microsoft.com/appx/manifest/foundation/windows10\"><Identity Name=\"{identity}\" Version=\"{version}\" /></Package>");
        }

        return packageStream.ToArray();
    }

    private static void DeleteDownloadDirectory(string packagePath)
    {
        if (string.IsNullOrWhiteSpace(packagePath))
        {
            return;
        }

        var directory = Directory.GetParent(packagePath)?.FullName;
        if (directory is not null && Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}
