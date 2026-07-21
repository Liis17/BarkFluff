using BarkFluff.Proto.Messages;

using MediatR;

namespace BarkFluff.Messages.Features.ApplyFederatedEdit;

public record ApplyFederatedEditCommand(ApplyFederatedEditRequest Request) : IRequest<ApplyFederatedEditResponse>;
