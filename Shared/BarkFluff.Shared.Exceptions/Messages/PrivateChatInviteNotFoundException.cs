namespace BarkFluff.Shared.Exceptions.Messages;

public class PrivateChatInviteNotFoundException : BaseGrpcException
{
    public override string ErrorCode => "7B19D8C2-1E2F-4F90-9F13-DCE8CC1A7F22";

    public override string ErrorMessage => "Приглашение в приватный чат не найдено";
}
