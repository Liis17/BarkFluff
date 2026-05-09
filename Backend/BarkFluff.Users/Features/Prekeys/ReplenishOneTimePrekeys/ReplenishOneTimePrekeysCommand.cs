using BarkFluff.Proto.Users;

using MediatR;

namespace BarkFluff.Users.Features.Prekeys.ReplenishOneTimePrekeys;

public class ReplenishOneTimePrekeysCommand : IRequest<ReplenishOneTimePrekeysResponse>
{
    public ReplenishOneTimePrekeysRequest Request { get; set; } = new();
}
