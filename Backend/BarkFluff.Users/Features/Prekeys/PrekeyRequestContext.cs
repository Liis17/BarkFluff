using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Shared.Identity;
using BarkFluff.Users.Persistence.Services;

using Grpc.Core;

namespace BarkFluff.Users.Features.Prekeys;

internal static class PrekeyRequestContext
{
    public static Guid RequireDeviceId(UserContext userContext)
    {
        if (!userContext.IsAuthenticated || userContext.TokenType != TokenType.User)
        {
            throw new PrekeyException(StatusCode.Unauthenticated, "Требуется авторизованное пользовательское устройство");
        }

        if (!Guid.TryParse(userContext.DeviceId, out var deviceId))
        {
            throw new PrekeyException(StatusCode.Unauthenticated, "В сессии отсутствует корректный DeviceId");
        }

        return deviceId;
    }
}
