namespace BarkFluff.Client.Core.Models;

public sealed record TestingPreferences
{
    public bool ShowIdsInProfile { get; init; }
    public bool ShowServerAddressesInAbout { get; init; }
    public bool SecretChatsEnabled { get; init; }
    public bool PrivateChatsEnabled { get; init; }
}
