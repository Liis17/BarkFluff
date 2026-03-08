namespace BarkFluff.Shared.Exceptions.Messages;

public class FileHasNotGroupPictureTypeException : BaseGrpcException
{
    public override string ErrorCode => "1ED8FF46-1CD7-4EFA-9CDD-8A07D18B2EE8";

    public override string ErrorMessage => "Переданный файл имеет отличный тип от Изображение чата";
}