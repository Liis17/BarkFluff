using BarkFluff.Files.Helpers;
using BarkFluff.GrpcServer.Settings;

namespace BarkFluff.Files.Tests.Helpers;

public class FileUrlHelperTests
{
    [Fact]
    public void GetPublicBaseUrl_WithExternalEndpoint_ReturnsWebPath()
    {
        var config = new Mock<IConfiguration>();
        config.Setup(c => c["ExternalEndpoint:Host"]).Returns("https://example.com");

        var result = FileUrlHelper.GetPublicBaseUrl(config.Object, new RunSettings());

        result.Should().Be("https://example.com/web");
    }

    [Fact]
    public void GetPublicBaseUrl_WithExternalEndpointTrailingSlash_ReturnsWithoutDoubleSlash()
    {
        var config = new Mock<IConfiguration>();
        config.Setup(c => c["ExternalEndpoint:Host"]).Returns("https://example.com/");

        var result = FileUrlHelper.GetPublicBaseUrl(config.Object, new RunSettings());

        result.Should().Be("https://example.com/web");
    }

    [Fact]
    public void GetPublicBaseUrl_NoExternalEndpoint_UsesRunSettings()
    {
        var config = new Mock<IConfiguration>();
        config.Setup(c => c["ExternalEndpoint:Host"]).Returns((string?)null);

        var settings = new RunSettings { Host = "http://192.168.1.1", Http1Port = 7005 };

        var result = FileUrlHelper.GetPublicBaseUrl(config.Object, settings);

        result.Should().Be("http://192.168.1.1:7005");
    }

    [Fact]
    public void GetPublicBaseUrl_NoExternalEndpointNoHost_UsesLocalhost()
    {
        var config = new Mock<IConfiguration>();
        config.Setup(c => c["ExternalEndpoint:Host"]).Returns((string?)null);

        var settings = new RunSettings { Http1Port = 7005 };

        var result = FileUrlHelper.GetPublicBaseUrl(config.Object, settings);

        result.Should().Be("http://localhost:7005");
    }

    [Fact]
    public void GenerateUploadUrl_CorrectFormat()
    {
        var id = Guid.Parse("12345678-1234-1234-1234-123456789abc");
        FileUrlHelper.GenerateUploadUrl("https://example.com/web", id)
            .Should().Be("https://example.com/web/upload/12345678-1234-1234-1234-123456789abc");
    }

    [Fact]
    public void GenerateDownloadUrl_CorrectFormat()
    {
        var id = Guid.Parse("12345678-1234-1234-1234-123456789abc");
        FileUrlHelper.GenerateDownloadUrl("https://example.com/web", id)
            .Should().Be("https://example.com/web/download/12345678-1234-1234-1234-123456789abc");
    }
}
