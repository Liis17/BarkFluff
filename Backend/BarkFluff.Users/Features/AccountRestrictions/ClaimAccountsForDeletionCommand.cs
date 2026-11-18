using BarkFluff.Proto.Users;

using MediatR;

namespace BarkFluff.Users.Features.AccountRestrictions;

public sealed record ClaimAccountsForDeletionCommand(int Limit) : IRequest<ClaimAccountsForDeletionResponse>;
