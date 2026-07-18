using MediatR;

namespace BarkFluff.Updates.Features.SubscribeMessagesRead;

public class ReadByNotification : INotification
{
    public Guid ChatId { get; set; }

    public long MessageId { get; set; }

    public List<long> NewReadBy { get; set; } = [];

    public List<long> NewReaders { get; set; } = [];

    public List<long> ChatMembers { get; set; } = [];

    public IReadOnlyList<long> ReadersWithNewRead => NewReaders.Count > 0
        ? NewReaders
        : NewReadBy;
}
