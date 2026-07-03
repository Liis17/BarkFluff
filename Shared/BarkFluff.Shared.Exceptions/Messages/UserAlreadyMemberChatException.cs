namespace BarkFluff.Shared.Exceptions.Messages;

public class UserAlreadyMemberChatException : BaseGrpcException
{
    public override string ErrorCode => "7D3F0A52-8C41-4E96-9B0D-2E5F8A1C4B77";

    public override string ErrorMessage => "Пользователь уже является участником этого чата";
}
