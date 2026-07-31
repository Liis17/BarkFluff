using MediatR;

namespace BarkFluff.Users.Features.Legal.AcceptLegalConsent
{
    public class AcceptLegalConsentCommand : IRequest
    {
        public string Revision { get; set; }
    }
}
