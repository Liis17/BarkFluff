namespace BarkFluff.Shared.Exceptions.Users;

public class ChatFolderNotFoundException : BaseGrpcException
{
    public override string ErrorCode => "5F0B7B2E-3F6E-4D2B-9B9E-9B7E7C2B9D8A";

    public override string ErrorMessage => "Папка чатов не найдена";
}
