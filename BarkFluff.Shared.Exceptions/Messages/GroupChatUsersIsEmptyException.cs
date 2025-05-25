namespace BarkFluff.Shared.Exceptions.Messages;

public class GroupChatUsersIsEmptyException : BaseGrpcException
{
    public override string ErrorCode => "DB4FBD5D-30D5-44F6-B0AD-5AF13239523F";

    public override string ErrorMessage =>
        "Необходимо указать хотя бы одного пользователя при создании группового чата";
}