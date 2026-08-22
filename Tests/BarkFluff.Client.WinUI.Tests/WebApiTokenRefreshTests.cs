using BarkFluff.Proto.Identity;
using BarkFluff.WebApi.Core.MessengerData;

using WebApiClient = BarkFluff.WebApi.Core.WebApi;

namespace BarkFluff.Client.WinUI.Tests;

public sealed class WebApiTokenRefreshTests
{
    [Fact]
    public async Task ForceRefreshTokenAsync_WithoutAccessToken_DoesNotReportSuccess()
    {
        using var webApi = new WebApiClient();
        var parameters = new GlobalParam
        {
            RefreshToken = new Token { Value = "refresh-token" }
        };

        var result = await webApi.ForceRefreshTokenAsync(parameters);

        Assert.False(result.IsSuccess);
        Assert.Null(parameters.AccessToken);
    }
}
