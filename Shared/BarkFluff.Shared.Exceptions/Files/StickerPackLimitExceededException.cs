namespace BarkFluff.Shared.Exceptions.Files;

public class StickerPackLimitExceededException : BaseGrpcException
{
    public override string ErrorCode => "4722AB66-DECD-430A-BCF5-B2D3787E687E";

    public override string ErrorMessage => "Достигнут лимит добавленных наборов стикеров";
}
