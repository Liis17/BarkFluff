using BarkFluff.Proto.Users;

using MediatR;

namespace BarkFluff.Users.Features.AccountRestrictions;

public sealed record SetAccountRestrictionCommand(long UserId, AccountRestrictionAction Action, string Reason)
    : IRequest<SetAccountRestrictionResponse>;
