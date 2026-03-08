namespace BarkFluff.Shared.Exceptions.Identity
{
    public class ResetIdNotFoundException : BaseGrpcException
    {
        public override string ErrorCode => "5B9A8269-617E-4D4C-9696-A554C59E3A86";

        public override string ErrorMessage => "Неверный идентификатор кода сброса пароля";
    }
}