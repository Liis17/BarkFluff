using BarkFluff.Web.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using System.Text;
using Xunit;

namespace BarkFluff.Web.Tests;

public sealed class GrpcWebTrailerCaptureTests
{
    [Fact]
    public void TrailerFrame_ContainsCapturedGrpcStatusAndErrorDetails()
    {
        var context = new DefaultHttpContext();
        var responseTrailers = new GrpcWebResponseTrailersFeature();
        context.Features.Set<IHttpResponseTrailersFeature>(responseTrailers);
        Assert.True(context.Response.SupportsTrailers());
        context.Response.AppendTrailer("grpc-status", "16");
        context.Response.AppendTrailer("grpc-message", "Invalid login or password");
        context.Response.AppendTrailer("x-error-code", "invalid_login_or_password");

        var trailers = responseTrailers.Trailers.ToDictionary(
            header => header.Key,
            header => header.Value.ToString(),
            StringComparer.OrdinalIgnoreCase);

        var encodedFrame = GrpcWebTrailerFrame.CreateFrame(trailers, base64: true);
        var frame = Convert.FromBase64String(Encoding.ASCII.GetString(encodedFrame));
        Assert.Equal(0x80, frame[0]);

        var dataLength = (frame[1] << 24) | (frame[2] << 16) | (frame[3] << 8) | frame[4];
        Assert.Equal(frame.Length - 5, dataLength);
        var trailerText = Encoding.UTF8.GetString(frame, 5, dataLength);
        Assert.Contains("grpc-status: 16\r\n", trailerText);
        Assert.Contains("x-error-code: invalid_login_or_password\r\n", trailerText);
    }
}
