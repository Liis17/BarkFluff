namespace BarkFluff.Shared.Exceptions.Users;

public class ChatFolderInvalidNameException : BaseGrpcException
{
    public override string ErrorCode => "8C1A6F4D-1B22-4E1B-8E4D-7E9A5B6C2A11";

    public override string ErrorMessage => "Название папки не должно быть пустым и не должно превышать 64 символа";
}
