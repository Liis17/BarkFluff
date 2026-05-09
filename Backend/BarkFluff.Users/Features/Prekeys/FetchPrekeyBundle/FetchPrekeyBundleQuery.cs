using BarkFluff.Proto.Users;

using MediatR;

namespace BarkFluff.Users.Features.Prekeys.FetchPrekeyBundle;

public class FetchPrekeyBundleQuery : IRequest<FetchPrekeyBundleResponse>
{
    public long UserId { get; set; }

    public string DeviceId { get; set; } = string.Empty;
}
