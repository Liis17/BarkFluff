namespace BarkFluff.Shared.Exceptions.Messages;

public class SourceForSendMessageNotSetException : BaseGrpcException
{
    public override string ErrorCode => "2E70077F-D3C6-41A4-9D4D-49A0004CD54D";

    public override string ErrorMessage => "Не указан chat_id или user_id для отправки сообщения";
}