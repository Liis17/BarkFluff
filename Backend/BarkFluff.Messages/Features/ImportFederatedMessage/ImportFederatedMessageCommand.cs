using BarkFluff.Proto.Messages;

using MediatR;

namespace BarkFluff.Messages.Features.ImportFederatedMessage;

public record ImportFederatedMessageCommand(ImportFederatedMessageRequest Request) : IRequest<ImportFederatedMessageResponse>;
