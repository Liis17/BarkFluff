using BarkFluff.Proto.Messages;

using MediatR;

namespace BarkFluff.Messages.Features.CheckFederatedPresenceAccess;

public class CheckFederatedPresenceAccessQuery : IRequest<CheckFederatedPresenceAccessResponse>
{
    // Вторая линия защиты от разрастания подписки: основной лимит живёт в Federation (этап 4.3).
    // Константа, а не конфиг — значение диктуется контрактом, а не эксплуатацией ноды.
    public const int MaxUserUuids = 500;

    public required string RequestingServer { get; init; }

    public required IReadOnlyList<string> UserUuids { get; init; }
}
