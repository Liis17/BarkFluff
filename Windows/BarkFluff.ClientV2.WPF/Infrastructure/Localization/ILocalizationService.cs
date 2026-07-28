namespace BarkFluff.ClientV2.WPF.Infrastructure.Localization;

public interface ILocalizationService
{
    string ResolveSupportedLanguage(string? requestedLanguage);

    void Apply(string language);

    string GetString(string resourceKey);
}
