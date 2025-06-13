using MediatR;

namespace BarkFluff.Users.Features.ChangeBio
{
    public class ChangeBioCommand : IRequest
    {
        public string Bio { get; set; }
    }
}