using MediatR;
using BarkFluff.Navigator.Domain;

namespace BarkFluff.Navigator.Features.RegisterServer;

public class RegisterServerCommand : IRequest<BarkFluff.Proto.Navigator.RegisterServerResponse>
{
    public ServerInfo Server { get; set; }
} 