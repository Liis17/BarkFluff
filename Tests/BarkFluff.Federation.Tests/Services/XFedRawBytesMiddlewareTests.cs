using BarkFluff.Federation.Services;
using BarkFluff.Proto.Federation;

using Google.Protobuf;

using Microsoft.AspNetCore.Http;

namespace BarkFluff.Federation.Tests.Services;

public class XFedRawBytesMiddlewareTests
{
    private static byte[] FrameMessage(byte[] message, byte compressedFlag = 0)
    {
        var framed = new byte[5 + message.Length];
        framed[0] = compressedFlag;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(framed.AsSpan(1, 4), (uint)message.Length);
        message.CopyTo(framed, 5);
        return framed;
    }

    private static DefaultHttpContext CreateContext(string path, byte[] body)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.Body = new MemoryStream(body);
        return context;
    }

    [Fact]
    public async Task InvokeAsync_ValidGrpcFrame_ExtractsMessageBytesAndRestoresBody()
    {
        var request = new PingRequest { OriginServer = "peer.test" };
        var messageBytes = request.ToByteArray();
        var framed = FrameMessage(messageBytes);

        var nextCalled = false;
        var middleware = new XFedRawBytesMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var context = CreateContext("/barkfluff.federation.FederationS2SApi/Ping", framed);
        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
        context.Items[XFedRawBytesMiddleware.ItemsKey].Should().BeOfType<byte[]>().Which.Should().Equal(messageBytes);

        // Тело реконструировано для штатного gRPC-парсинга ниже по пайплайну.
        using var reread = new MemoryStream();
        await context.Request.Body.CopyToAsync(reread);
        reread.ToArray().Should().Equal(framed);
    }

    [Fact]
    public async Task InvokeAsync_GetServerKeys_IsExempt_NoBytesExtracted()
    {
        var middleware = new XFedRawBytesMiddleware(_ => Task.CompletedTask);
        var context = CreateContext("/barkfluff.federation.FederationS2SApi/GetServerKeys", FrameMessage([1, 2, 3]));

        await middleware.InvokeAsync(context);

        context.Items.Should().NotContainKey(XFedRawBytesMiddleware.ItemsKey);
    }

    [Fact]
    public async Task InvokeAsync_OtherServicePath_NoBytesExtracted()
    {
        var middleware = new XFedRawBytesMiddleware(_ => Task.CompletedTask);
        var context = CreateContext("/barkfluff.federation_internal.FederationInternalApi/GetFederationStatus", FrameMessage([1, 2, 3]));

        await middleware.InvokeAsync(context);

        context.Items.Should().NotContainKey(XFedRawBytesMiddleware.ItemsKey);
    }

    [Fact]
    public async Task InvokeAsync_CompressedFrame_NoBytesExtracted()
    {
        var middleware = new XFedRawBytesMiddleware(_ => Task.CompletedTask);
        var context = CreateContext("/barkfluff.federation.FederationS2SApi/Ping", FrameMessage([1, 2, 3], compressedFlag: 1));

        await middleware.InvokeAsync(context);

        context.Items.Should().NotContainKey(XFedRawBytesMiddleware.ItemsKey);
    }

    [Fact]
    public async Task InvokeAsync_TooShortBody_NoBytesExtracted()
    {
        var middleware = new XFedRawBytesMiddleware(_ => Task.CompletedTask);
        var context = CreateContext("/barkfluff.federation.FederationS2SApi/Ping", [0, 0, 0]);

        await middleware.InvokeAsync(context);

        context.Items.Should().NotContainKey(XFedRawBytesMiddleware.ItemsKey);
    }

    [Fact]
    public async Task InvokeAsync_DeclaredLengthExceedsBody_NoBytesExtracted()
    {
        var message = new byte[] { 1, 2, 3 };
        var framed = FrameMessage(message);
        // Портим заголовок: заявленная длина больше фактической.
        framed[4] = 100;

        var middleware = new XFedRawBytesMiddleware(_ => Task.CompletedTask);
        var context = CreateContext("/barkfluff.federation.FederationS2SApi/Ping", framed);

        await middleware.InvokeAsync(context);

        context.Items.Should().NotContainKey(XFedRawBytesMiddleware.ItemsKey);
    }
}
