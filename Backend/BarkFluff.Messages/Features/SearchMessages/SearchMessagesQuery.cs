using BarkFluff.Messages.Persistence.Services.Dtos;
using BarkFluff.Proto.Messages;
using MediatR;

namespace BarkFluff.Messages.Features.SearchMessages;

public sealed class SearchMessagesQuery : IRequest<SearchMessagesResponse>
{
    public MessageSearchFilter Filter { get; init; } = new();
}
