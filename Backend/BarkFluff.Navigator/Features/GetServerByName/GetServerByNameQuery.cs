using MediatR;

namespace BarkFluff.Navigator.Features.GetServerByName;

public class GetServerByNameQuery : IRequest<BarkFluff.Proto.Navigator.GetServerByNameResponse>
{
    public required string ServerName { get; set; }
}
