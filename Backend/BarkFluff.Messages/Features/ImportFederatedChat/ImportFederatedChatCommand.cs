using BarkFluff.Proto.Messages;

using MediatR;

namespace BarkFluff.Messages.Features.ImportFederatedChat;

public record ImportFederatedChatCommand(ImportFederatedChatRequest Request) : IRequest<ImportFederatedChatResponse>;
