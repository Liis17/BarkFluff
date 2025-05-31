namespace BarkFluff.Shared.Exceptions.Messages;

public class IsNotGroupChatException : BaseGrpcException
{
    public override string ErrorCode => "DF5F9672-E0D6-4D6D-AC68-9D0B666ADD1E";

    public override string ErrorMessage => "Это действие доступно только для групповых чатов";
}