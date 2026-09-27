using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using System.Text;

namespace BarkFluff.Web.Infrastructure;

internal sealed class GrpcWebResponseTrailersFeature : IHttpResponseTrailersFeature
{
    public IHeaderDictionary Trailers { get; set; } = new HeaderDictionary();
}

internal static class GrpcWebTrailerFrame
{
    internal static byte[] CreateFrame(IReadOnlyDictionary<string, string> trailers, bool base64)
    {
        var text = new StringBuilder();
        foreach (var (key, value) in trailers)
            text.Append(key).Append(": ").Append(value).Append("\r\n");

        var data = Encoding.UTF8.GetBytes(text.ToString());
        var frame = new byte[5 + data.Length];
        frame[0] = 0x80;
        var length = BitConverter.GetBytes((uint)data.Length);
        if (BitConverter.IsLittleEndian) Array.Reverse(length);
        length.CopyTo(frame, 1);
        data.CopyTo(frame, 5);

        return base64
            ? Encoding.ASCII.GetBytes(Convert.ToBase64String(frame))
            : frame;
    }
}
