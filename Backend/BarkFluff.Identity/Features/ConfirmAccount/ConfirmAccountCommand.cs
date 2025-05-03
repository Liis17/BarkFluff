using BarkFluff.Identity.Features.Shared;
using BarkFluff.Proto.Identity;
using MediatR;

namespace BarkFluff.Identity.Features.ConfirmAccount;

public class ConfirmAccountCommand : DeviceNameCommand, IRequest<ConfirmAccountResponse>
{
    public string Code { get; set; }
    
    public string CodeId { get; set; }
}