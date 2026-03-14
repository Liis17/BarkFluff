using BarkFluff.Proto.Configuration;

using MediatR;

namespace BarkFluff.Configuration.Features.DeleteReservedName;

public class DeleteReservedNameCommand : IRequest<DeleteReservedNameResponse>
{
    public string Name { get; set; }
}
