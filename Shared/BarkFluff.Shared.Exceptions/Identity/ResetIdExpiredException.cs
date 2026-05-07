namespace BarkFluff.Shared.Exceptions.Identity
{
    public class ResetIdExpiredException : BaseGrpcException
    {
        public override string ErrorCode => "9F3D1B82-8E55-4C71-BD2A-3D7FAC2E6AE1";

        public override string ErrorMessage => "Срок действия идентификатора сброса пароля истёк";
    }
}
