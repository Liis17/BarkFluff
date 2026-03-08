namespace BarkFluff.Shared.Exceptions.Messages;

public class MessageNotContainContextException : BaseGrpcException
{
    public override string ErrorCode => "C45A0486-AEBE-4A7F-B9BD-42BCEA4F843F";

    public override string ErrorMessage => "Сообщение должно содержать хотя бы текст или вложения";
}