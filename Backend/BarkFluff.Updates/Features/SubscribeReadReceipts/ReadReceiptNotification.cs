namespace BarkFluff.Updates.Features.SubscribeReadReceipts;

using MediatR;

public record ReadReceiptNotification(
    string ChatId,
    long MessageId,
    List<long> ReadBy,
    List<long> ChatMembers,
    bool IsLastMessage,
    DateTime ReadAt) : INotification;
