namespace BarkFluff.Shared.Exceptions.Messages;

// Fed-ChatCreated пришёл для invitee, которого нет на этой ноде / деактивирован / является remote.
// Permanent отказ: Federation не повторяет доставку (docs/rearch/05, шаг 1 ImportFederatedChat).
public class UnknownInviteeException : BaseGrpcException
{
    public override string ErrorCode => "B2F4D7E1-9A6C-4D3E-8B15-2A7C9F4D1B02";
    public override string ErrorMessage => "Получатель федеративного чата не найден";
}
