using BarkFluff.Proto.Identity;

using MediatR;

namespace BarkFluff.Identity.Features.SetAccountRestrictionServer;

public sealed class SetAccountRestrictionServerCommand : IRequest<SetAccountRestrictionServerResponse>
{
    public long UserId { get; init; }

    public BarkFluff.Proto.Users.AccountRestrictionAction Action { get; init; }

    public string Reason { get; init; } = string.Empty;
}
