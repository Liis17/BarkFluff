namespace BarkFluff.Client.Core.Infrastructure.Localization;

public interface ILocalizationService
{
    string ResolveSupportedLanguage(string? requestedLanguage);

    void Apply(string language);

    string GetString(string resourceKey);
}
