namespace BarkFluff.Shared.Exceptions.Identity;

public class UsernameInvalidFormatException : BaseGrpcException
{
    public override string ErrorCode => "E7A4C9D2-3B61-4F82-A5E0-9C1D8F2B6A47";

    public override string ErrorMessage => "Имя пользователя имеет недопустимый формат: разрешены латинские буквы, цифры и подчёркивание, длина от 3 до 32 символов";
}
