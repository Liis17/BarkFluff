namespace BarkFluff.Shared.Exceptions.Messages;

public class SecretInviteNotFoundException : BaseGrpcException
{
    public override string ErrorCode => "B12F7A45-3D8E-4B27-AE33-1C5E0F4F2A8E";

    public override string ErrorMessage => "Инвайт секретного чата не найден или истёк";
}
