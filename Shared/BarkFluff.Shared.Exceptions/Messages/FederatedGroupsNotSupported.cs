namespace BarkFluff.Shared.Exceptions.Messages;

// Попытка добавить remote-участника (по FID/uuid) в группу или создать федеративную группу
// (docs/rearch/05, «Ограничения MVP»: только 1-на-1).
public class FederatedGroupsNotSupported : BaseGrpcException
{
    public override string ErrorCode => "F6D8B1C5-4EAF-4F9D-DF5C-6EB1A8C5F06";
    public override string ErrorMessage => "Федеративные групповые чаты не поддерживаются";
}
