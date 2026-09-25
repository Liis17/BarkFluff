namespace BarkFluff.Identity.Settings;

public sealed class TelegramAuthOptions
{
    public bool Enabled { get; set; }
    public string BotToken { get; set; } = "";
    public string NodeName { get; set; } = "BarkFluff";
    public bool Configured => Enabled && !string.IsNullOrWhiteSpace(BotToken);
}
