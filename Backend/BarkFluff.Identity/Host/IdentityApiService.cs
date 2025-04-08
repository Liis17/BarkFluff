using BarkFluff.Proto.Identity;
using BarkFluff.Shared.Exceptions.Identity;
using Grpc.Core;

namespace BarkFluff.Identity.Host;

public class IdentityApiService : BarkFluff.Proto.Identity.IdentityApi.IdentityApiBase
{
    public override Task<AuthResponse> Auth(AuthRequest request, ServerCallContext context)
    {

        throw new EmailExistException();
        
        return base.Auth(request, context);
    }

    public override Task<CreateTokenResponse> CreateToken(CreateTokenRequest request, ServerCallContext context)
    {
        return base.CreateToken(request, context);
    }

    public override Task<CreateAccountResponse> CreateAccount(CreateAccountRequest request, ServerCallContext context)
    {
        return base.CreateAccount(request, context);
    }

    public override Task<ConfirmAccountResponse> ConfirmAccount(ConfirmAccountRequest request, ServerCallContext context)
    {
        return base.ConfirmAccount(request, context);
    }

    public override Task<SetPasswordResponse> SetPassword(SetPasswordRequest request, ServerCallContext context)
    {
        return base.SetPassword(request, context);
    }
}