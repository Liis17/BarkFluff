namespace BarkFluff.Shared.Exceptions.Messages;

public class GroupChatTitleIsEmptyException : BaseGrpcException
{
    public override string ErrorCode => "0071F324-D75B-4AE9-93B4-BA62BD61AAEF";
    
    public override string Message => "Заголовок группового чата не может быть пустым";
}