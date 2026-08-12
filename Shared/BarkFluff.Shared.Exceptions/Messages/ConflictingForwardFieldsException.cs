using Grpc.Core;

namespace BarkFluff.Shared.Exceptions.Messages;

/// <summary>
/// Клиент прислал одновременно устаревшее <c>forwarded_message_id</c> и новые
/// <c>reply_to_message_id</c>/<c>forwarded_message_ids</c>. Это не состояние данных, а ошибка
/// вызывающего — отсюда InvalidArgument вместо дефолтного FailedPrecondition.
/// </summary>
public class ConflictingForwardFieldsException : BaseGrpcException
{
    public override string ErrorCode => "8F2D6A05-91C4-4B7E-A3D8-6E0B9C5F1A72";

    public override string ErrorMessage =>
        "Нельзя одновременно использовать forwarded_message_id и reply_to_message_id/forwarded_message_ids";

    public override StatusCode StatusCode => StatusCode.InvalidArgument;
}
