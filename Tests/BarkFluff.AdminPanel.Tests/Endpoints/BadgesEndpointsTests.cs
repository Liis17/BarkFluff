using Barkfluff.AdminPanel.Endpoints;

using Grpc.Core;

using Microsoft.AspNetCore.Http;

using Xunit;

namespace BarkFluff.AdminPanel.Tests.Endpoints;

public class BadgesEndpointsTests
{
    [Fact]
    public void ValidateCreateBadgeImages_RequiresBothImages()
    {
        using var fullStream = new MemoryStream([1]);
        using var miniStream = new MemoryStream([2]);
        var fullImage = new FormFile(fullStream, 0, 1, "image", "full.png");
        var miniImage = new FormFile(miniStream, 0, 1, "miniImage", "mini.png");

        const string expectedError = "Для создания бейджа нужны полная картинка и миниатюра";
        Assert.Equal(expectedError, BadgesEndpoints.ValidateCreateBadgeImages(null, miniImage));
        Assert.Equal(expectedError, BadgesEndpoints.ValidateCreateBadgeImages(fullImage, null));
        Assert.Null(BadgesEndpoints.ValidateCreateBadgeImages(fullImage, miniImage));
    }

    [Fact]
    public void InvalidBadgeImageResponse_ReturnsBadRequestWithFilesValidationText()
    {
        const string detail = "Для этого вида бейджа требуется изображение 32×32 пикселей";
        var result = BadgesEndpoints.InvalidBadgeImageResponse(
            new RpcException(new Status(StatusCode.InvalidArgument, detail)));

        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        Assert.Equal(detail, Assert.IsAssignableFrom<IValueHttpResult>(result).Value);
    }

    [Fact]
    public void ResolveBadgeImageUrl_PreservesExistingUrlWithoutNewImage()
    {
        Assert.Equal(
            "https://example.test/current.png",
            BadgesEndpoints.ResolveBadgeImageUrl(null, string.Empty, "https://example.test/current.png"));
    }

    [Fact]
    public void ResolveBadgeImageUrl_UsesUploadedOrExplicitUrlWhenProvided()
    {
        Assert.Equal(
            "https://example.test/uploaded.png",
            BadgesEndpoints.ResolveBadgeImageUrl("https://example.test/uploaded.png", "https://example.test/requested.png", "https://example.test/current.png"));
        Assert.Equal(
            "https://example.test/requested.png",
            BadgesEndpoints.ResolveBadgeImageUrl(null, "https://example.test/requested.png", "https://example.test/current.png"));
    }
}
