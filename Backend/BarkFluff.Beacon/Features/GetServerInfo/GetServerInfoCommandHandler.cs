using BarkFluff.Proto.Beacon;
using MediatR;

namespace BarkFluff.Beacon.Features.GetServerInfo;

public class GetServerInfoCommandHandler : IRequestHandler<GetServerInfoCommand, GetServerInfoResponse>
{
    public async Task<GetServerInfoResponse> Handle(GetServerInfoCommand request, CancellationToken cancellationToken)
    {
        return new GetServerInfoResponse()
        {
            Name = "my best server",
            
            Beacon = new Service
            {
                Endpoint = new ServiceEndpoint()
                {
                    Host = "fooxboy.ru",
                    Port = 7002,
                },
                Name = "beacon",
                Status = ServiceStatus.Healthy,
                TlsEnabled = false
            },
            Identity = new Service
            {
                Endpoint = new ServiceEndpoint()
                {
                    Host = "fooxboy.ru",
                    Port = 7000,
                },
                Name = "identity",
                Status = ServiceStatus.Healthy,
                TlsEnabled = false
            },
            Notifications = new Service
            {
                Endpoint = new ServiceEndpoint()
                {
                    Host = "localhost",
                    Port = 1234,
                },
                Name = "notifications",
                Status = ServiceStatus.Healthy,
                TlsEnabled = false
            },
            Users = new Service
            {
                Endpoint = new ServiceEndpoint()
                {
                    Host = "fooxboy.ru",
                    Port = 7001,
                },
                Name = "users",
                Status = ServiceStatus.Healthy,
                TlsEnabled = false
            }
        };
    }
}