using BarkFluff.Proto.Messages;

using MediatR;

namespace BarkFluff.Messages.Features.ExportData;

public class GetUserAllMessagesQuery : IRequest<GetUserAllMessagesResponse>
{
    public long UserId { get; init; }
}
