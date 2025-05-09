using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Identity.Persistence.Services;
using BarkFluff.Proto.Identity;
using BarkFluff.Shared.Exceptions.Identity;
using MediatR;
using OtpNet;
using OtpNotCreatedException = BarkFluff.Identity.Persistence.Exceptions.OtpNotCreatedException;

namespace BarkFluff.Identity.Features.ConfirmOtpVerification;

public class ConfirmOtpVerificationCommandHandler : IRequestHandler<ConfirmOtpVerificationCommand, ConfirmOtpVerificationResponse>
{
    private readonly UserContext _userContext;
    private readonly AuthPropertiesStorage _authPropertiesStorage;

    public ConfirmOtpVerificationCommandHandler(UserContext userContext, AuthPropertiesStorage authPropertiesStorage)
    {
        _userContext = userContext;
        _authPropertiesStorage = authPropertiesStorage;
    }

    public async Task<ConfirmOtpVerificationResponse> Handle(ConfirmOtpVerificationCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var otpSecret = await _authPropertiesStorage.GetOtpSecretKey(_userContext.UserId);
            
            var totp = new Totp(Base32Encoding.ToBytes(otpSecret));
            
            var isValid = totp.VerifyTotp(request.OtpCode, out long timeStepMatched, VerificationWindow.RfcSpecifiedNetworkDelay);

            if (!isValid)
            {
                throw new NotValidOtpCodeException();
            }

            await _authPropertiesStorage.EnableOtp(_userContext.UserId);
        }
        catch (OtpNotCreatedException ex)
        {
            throw new BarkFluff.Shared.Exceptions.Identity.OtpNotCreatedException();
        }

        return new ConfirmOtpVerificationResponse();
    }
}