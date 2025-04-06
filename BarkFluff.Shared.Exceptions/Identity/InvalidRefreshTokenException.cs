namespace BarkFluff.Shared.Exceptions.Identity;

public class InvalidRefreshTokenException : BaseGrpcException
{
    public override string ErrorCode => "7E6A31C5-3C4D-412E-87BC-0A387617A5D3";
    
    public override string ErrorMessage => nameof(InvalidRefreshTokenException);
}