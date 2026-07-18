namespace BarkFluff.Shared.Exceptions.Navigator;

public class FederationRegistrationRejectedException : BaseGrpcException
{
    public override string ErrorCode => "1CD8A150-5943-4C72-8AA5-7829C72823D1";
    public override string ErrorMessage => "Регистрация с server_name отклонена: well-known недоступен или ключи не совпадают";
}
