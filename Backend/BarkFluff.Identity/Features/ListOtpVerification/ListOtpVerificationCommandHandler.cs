using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Identity.Persistence.Exceptions;
using BarkFluff.Identity.Persistence.Services;
using BarkFluff.Proto.Identity;
using MediatR;

namespace BarkFluff.Identity.Features.ListOtpVerification;

public class ListOtpVerificationCommandHandler : IRequestHandler<ListOtpVerificationCommand, ListOtpVerificationResponse>
{
    
    private readonly UserContext _userContext;
    
    private readonly AuthPropertiesStorage _authPropertiesStorage;

    public ListOtpVerificationCommandHandler(UserContext userContext, AuthPropertiesStorage authPropertiesStorage)
    {
        _userContext = userContext;
        _authPropertiesStorage = authPropertiesStorage;
    }

    public async Task<ListOtpVerificationResponse> Handle(ListOtpVerificationCommand request, CancellationToken cancellationToken)
    {
        var otpAuth = await _authPropertiesStorage.GetUserAuthProperties(_userContext.UserId);

        if (otpAuth is null)
        {
            throw new OtpNotCreatedException();
        }

        return new ListOtpVerificationResponse()
        {
            EmailEnabled = otpAuth.EmailOtpEnabled,
            AuthenticatorEnabled = otpAuth.OtpEnabled,
        };
    }
}