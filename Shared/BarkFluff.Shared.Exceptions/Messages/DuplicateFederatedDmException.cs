namespace BarkFluff.Shared.Exceptions.Messages;

// Активный fed-DM этой UUID-пары уже существует с другим ChatId (docs/rearch/05, «Создание чата»).
// Permanent отказ. Протокол слияния (docs/rearch/phase-2/step-2.7) заменяет этот ответ на merge.
public class DuplicateFederatedDmException : BaseGrpcException
{
    public override string ErrorCode => "C3A5E8F2-1B7D-4E6F-AC29-3B8D1E5F2C03";
    public override string ErrorMessage => "Федеративный чат этой пары уже существует";
}
