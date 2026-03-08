using BarkFluff.Navigator.Domain;

using MediatR;

namespace BarkFluff.Navigator.Features.RegisterServer;

public class RegisterServerCommand : IRequest<BarkFluff.Proto.Navigator.RegisterServerResponse>
{
    public ServerInfo Server { get; set; }
}