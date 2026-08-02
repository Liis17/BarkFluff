using System.Globalization;
using System.Windows;

namespace BarkFluff.ClientV2.WPF.Infrastructure.Localization;

public sealed class LocalizationService : ILocalizationService
{
    public string ResolveSupportedLanguage(string? requestedLanguage)
    {
        var candidate = requestedLanguage?.ToLowerInvariant();
        if (candidate is "ru" or "en")
        {
            return candidate;
        }

        return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru" ? "ru" : "en";
    }

    public void Apply(string language)
    {
        var resources = Application.Current.Resources.MergedDictionaries;
        var existingDictionary = resources.FirstOrDefault(dictionary =>
            dictionary.Source?.OriginalString.Contains("/Localization/Strings.", StringComparison.OrdinalIgnoreCase) == true);

        if (existingDictionary is not null)
        {
            resources.Remove(existingDictionary);
        }

        resources.Add(new ResourceDictionary
        {
            Source = new Uri($"/BarkFluff.ClientV2.WPF;component/Resources/Localization/Strings.{language}.xaml", UriKind.Relative)
        });

        var culture = new CultureInfo(language);
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }

    public string GetString(string resourceKey) =>
        Application.Current.TryFindResource(resourceKey) as string ?? resourceKey;
}
