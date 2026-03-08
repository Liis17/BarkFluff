namespace BarkFluff.Shared.Exceptions.Users;

public class ProfilePictureHasNotValidType : BaseGrpcException
{
    public override string ErrorCode => "7097703F-977C-4E28-8C85-1A287B3FF8AD";

    public override string ErrorMessage =>
        "Переданный file-id содержит файл не с типом Изображение профиля пользователя.";
}