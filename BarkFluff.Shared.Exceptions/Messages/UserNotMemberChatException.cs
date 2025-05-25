namespace BarkFluff.Shared.Exceptions.Messages;

public class UserNotMemberChatException : BaseGrpcException
{
    public override string ErrorCode => "CA1008EF-9487-4E37-A74A-C9B921F1D6CE";

    public override string ErrorMessage => "Пользователь не является участником этого чата";
}