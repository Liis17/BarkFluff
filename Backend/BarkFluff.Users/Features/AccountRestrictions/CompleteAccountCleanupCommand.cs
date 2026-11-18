using MediatR;

namespace BarkFluff.Users.Features.AccountRestrictions;

public sealed record CompleteAccountCleanupCommand(long UserId) : IRequest;
