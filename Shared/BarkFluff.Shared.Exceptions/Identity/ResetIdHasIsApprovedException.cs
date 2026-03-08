namespace BarkFluff.Shared.Exceptions.Identity
{
    public class ResetIdHasIsApprovedException : BaseGrpcException
    {
        public override string ErrorCode => "BE708516-BF40-44F9-A6D1-A7F30AB02BED";

        public override string ErrorMessage => "Невозможно повторно сбросить пароль по этому идентификатору сброса";
    }
}