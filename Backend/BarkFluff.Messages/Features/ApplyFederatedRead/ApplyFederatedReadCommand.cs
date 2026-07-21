using BarkFluff.Proto.Messages;

using MediatR;

namespace BarkFluff.Messages.Features.ApplyFederatedRead;

public record ApplyFederatedReadCommand(ApplyFederatedReadRequest Request) : IRequest<ApplyFederatedReadResponse>;
