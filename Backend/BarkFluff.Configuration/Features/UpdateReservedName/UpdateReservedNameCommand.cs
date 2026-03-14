using BarkFluff.Proto.Configuration;

using MediatR;

namespace BarkFluff.Configuration.Features.UpdateReservedName;

public class UpdateReservedNameCommand : IRequest<UpdateReservedNameResponse>
{
    public string OldName { get; set; }
    public string NewName { get; set; }
}
