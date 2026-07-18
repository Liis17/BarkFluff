using System.Buffers.Binary;

namespace BarkFluff.Federation.Services;

// Перехватывает сырые wire-байты unary-запроса ДО protobuf-десериализации (докЗ02: "request-bytes —
// это полученные wire-байты... пере-сериализация на проверке запрещена"). Grpc.AspNetCore-интерсепторы
// видят уже распарсенное сообщение (см. Context7 aspnetcore.docs, "gRPC Interceptors versus
// Middleware"), поэтому байты снимаются здесь, на уровне ASP.NET Core middleware, и кладутся
// в HttpContext.Items — читает их XFedServerInterceptor.
//
// gRPC message framing (Length-Prefixed-Message, grpc/PROTOCOL-HTTP2.md):
// 1 байт Compressed-Flag + 4 байта Message-Length (big endian) + Message. Ни один сервис платформы
// не включает per-message compression, поэтому Compressed-Flag ожидается 0; иное — не удаётся
// извлечь байты, интерсептор ниже провалит проверку подписи как "не удалось получить сырые байты".
public class XFedRawBytesMiddleware
{
    public const string ItemsKey = "xfed-raw-request-bytes";

    private const string ServicePathPrefix = "/barkfluff.federation.FederationS2SApi/";
    private const string ExemptMethodSuffix = "/GetServerKeys";

    private readonly RequestDelegate _next;

    public XFedRawBytesMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        if (!path.StartsWith(ServicePathPrefix, StringComparison.Ordinal) || path.EndsWith(ExemptMethodSuffix, StringComparison.Ordinal))
        {
            await _next(context);
            return;
        }

        using var buffer = new MemoryStream();
        await context.Request.Body.CopyToAsync(buffer);
        var bodyBytes = buffer.ToArray();

        if (bodyBytes.Length >= 5)
        {
            var compressedFlag = bodyBytes[0];
            var messageLength = BinaryPrimitives.ReadUInt32BigEndian(bodyBytes.AsSpan(1, 4));

            if (compressedFlag == 0 && messageLength <= bodyBytes.Length - 5)
            {
                context.Items[ItemsKey] = bodyBytes.AsSpan(5, (int)messageLength).ToArray();
            }
        }

        context.Request.Body = new MemoryStream(bodyBytes) { Position = 0 };

        await _next(context);
    }
}
