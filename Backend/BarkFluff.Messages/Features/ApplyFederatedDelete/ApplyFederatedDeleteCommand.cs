using BarkFluff.Proto.Messages;

using MediatR;

namespace BarkFluff.Messages.Features.ApplyFederatedDelete;

public record ApplyFederatedDeleteCommand(ApplyFederatedDeleteRequest Request) : IRequest<ApplyFederatedDeleteResponse>;
