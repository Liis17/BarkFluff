namespace BarkFluff.Client.Core.Services;

public interface IChatTypingService
{
    event Action? Changed;
    IReadOnlyCollection<long> TypingUserIds { get; }
    void Watch(string? chatId);
    void Input(bool hasText);
    void StopSending();
    Task StopAsync();
}
