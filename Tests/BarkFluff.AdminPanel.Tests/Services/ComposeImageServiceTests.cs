using Barkfluff.AdminPanel.Services;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

using Xunit;

namespace BarkFluff.AdminPanel.Tests.Services;

public class ComposeImageServiceTests
{
    private const string Compose = """
version: '3.8'

x-common-variables: &common-variables
  CONFIGURATION_SERVICE_URL: "${CONFIGURATION_SERVICE_URL}"

services:
  beacon:
    image: docker.barkfluff.com/barkfluff-beacon-nightly:latest
    container_name: beacon
    restart: always

  cloud-messaging:
    image: docker.barkfluff.com/barkfluff-cloud-messaging-nightly:1.0.7
    container_name: cloud-messaging

  admin-panel:
    image: docker.barkfluff.com/barkfluff-admin-panel-nightly:latest
    container_name: admin-panel
    volumes:
      - ./docker-compose.yml:/docker-compose.yml:rw

  seq:
    image: datalust/seq:latest
    container_name: seq

  #minio:
  #  image: quay.io/minio/minio:RELEASE.2025-04-22T22-12-26Z-cpuv1

  postgres:
    image: postgres:18

networks:
  barkfluff-network:
    driver: bridge
""";

    [Fact]
    public void Parse_ReturnsOnlyBarkFluffImages()
    {
        var images = ComposeImageService.Parse(Compose);

        Assert.Equal(new[] { "admin-panel", "beacon", "cloud-messaging" }, images.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
        Assert.Equal("barkfluff-cloud-messaging", images["cloud-messaging"].BaseRepository);
        Assert.Equal("nightly", images["cloud-messaging"].Branch);
        Assert.Equal("1.0.7", images["cloud-messaging"].Tag);
    }

    [Theory]
    [InlineData("dev", "docker.barkfluff.com/barkfluff-beacon-dev:latest")]
    [InlineData("master", "docker.barkfluff.com/barkfluff-beacon:latest")]
    public void TryRewrite_ReplacesOnlyTargetServiceImage(string branch, string expectedImage)
    {
        Assert.True(ComposeImageService.TryRewrite(Compose, "beacon", branch, out var result, out var error));
        Assert.Null(error);

        var images = ComposeImageService.Parse(result);
        Assert.Equal(branch, images["beacon"].Branch);
        Assert.Contains($"    image: {expectedImage}", result);

        // Остальные строки не тронуты
        var originalLines = Compose.Split('\n');
        var updatedLines = result.Split('\n');
        Assert.Equal(originalLines.Length, updatedLines.Length);
        for (var i = 0; i < originalLines.Length; i++)
        {
            if (originalLines[i].Contains("barkfluff-beacon", StringComparison.Ordinal))
                continue;
            Assert.Equal(originalLines[i], updatedLines[i]);
        }
    }

    [Fact]
    public void TryRewrite_KeepsSemverTagAndSwitchesMultiWordRepository()
    {
        Assert.True(ComposeImageService.TryRewrite(Compose, "cloud-messaging", "master", out var result, out _));

        Assert.Contains("image: docker.barkfluff.com/barkfluff-cloud-messaging:1.0.7", result);
        Assert.DoesNotContain("barkfluff-cloud-messaging-nightly", result);
    }

    [Fact]
    public void TryRewrite_SameBranchReturnsUnchangedContent()
    {
        Assert.True(ComposeImageService.TryRewrite(Compose, "beacon", "nightly", out var result, out _));

        Assert.Equal(Compose, result);
    }

    [Fact]
    public void TryRewrite_PreservesCrlfAndTrailingContent()
    {
        var crlf = Compose.Replace("\n", "\r\n");

        Assert.True(ComposeImageService.TryRewrite(crlf, "beacon", "dev", out var result, out _));

        Assert.Equal(crlf.Split("\r\n").Length, result.Split("\r\n").Length);
        Assert.Equal(crlf.Count(c => c == '\n'), result.Count(c => c == '\n'));
        Assert.Contains("image: docker.barkfluff.com/barkfluff-beacon-dev:latest\r\n", result);
    }

    [Fact]
    public void TryRewrite_UnknownServiceFails()
    {
        Assert.False(ComposeImageService.TryRewrite(Compose, "seq", "dev", out var result, out var error));

        Assert.Equal(Compose, result);
        Assert.Contains("seq", error!);
    }

    [Fact]
    public void TryRewrite_UnknownBranchFails()
    {
        Assert.False(ComposeImageService.TryRewrite(Compose, "beacon", "feature-x", out var result, out var error));

        Assert.Equal(Compose, result);
        Assert.Contains("feature-x", error!);
    }

    [Fact]
    public async Task SetBranchAsync_WritesFileInPlaceAndRestoreReverts()
    {
        var directory = Directory.CreateTempSubdirectory("compose-image-service");
        try
        {
            var composePath = Path.Combine(directory.FullName, "docker-compose.yml");
            await File.WriteAllTextAsync(composePath, Compose);

            // Открытый до правки дескриптор увидит новое содержимое, только если запись прошла в тот же
            // inode — от этого зависит bind mount одного файла в контейнер admin-panel
            await using var openedBeforeWrite = new FileStream(composePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

            var service = CreateService(composePath, Path.Combine(directory.FullName, "backups"));

            var previous = await service.SetBranchAsync("beacon", "dev");

            Assert.Equal(Compose, previous);
            Assert.Contains("barkfluff-beacon-dev:latest", await File.ReadAllTextAsync(composePath));

            openedBeforeWrite.Position = 0;
            using var reader = new StreamReader(openedBeforeWrite, leaveOpen: true);
            Assert.Contains("barkfluff-beacon-dev:latest", await reader.ReadToEndAsync());

            Assert.Single(Directory.GetFiles(Path.Combine(directory.FullName, "backups")));

            var images = await service.GetImagesAsync();
            Assert.Equal("dev", images["beacon"].Branch);

            await service.RestoreAsync(previous);
            Assert.Equal(Compose, await File.ReadAllTextAsync(composePath));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static ComposeImageService CreateService(string composePath, string backupDirectory)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Docker:ComposeFile"] = composePath,
                ["Docker:ComposeBackupDirectory"] = backupDirectory
            })
            .Build();

        return new ComposeImageService(configuration, NullLogger<ComposeImageService>.Instance);
    }

    [Theory]
    [InlineData("docker.barkfluff.com/barkfluff-users-dev:latest", "dev")]
    [InlineData("docker.barkfluff.com/barkfluff-users-nightly:1.0.3", "nightly")]
    [InlineData("docker.barkfluff.com/barkfluff-users:latest", "master")]
    [InlineData("datalust/seq:latest", null)]
    public void BranchFromImage_DetectsSuffix(string image, string? expected)
    {
        Assert.Equal(expected, ComposeImageService.BranchFromImage(image));
    }
}
