using BarkFluff.Proto.Configuration;

using MediatR;

namespace BarkFluff.Configuration.Features.AddReservedName;

public class AddReservedNameCommand : IRequest<AddReservedNameResponse>
{
    public string Name { get; set; }
}
