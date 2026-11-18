using BarkFluff.Proto.Users;

using MediatR;

namespace BarkFluff.Users.Features.AccountRestrictions;

public sealed record GetAccountRestrictionQuery(long UserId) : IRequest<GetAccountRestrictionResponse>;
