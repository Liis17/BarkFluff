using BarkFluff.Client.Core.Infrastructure.Localization;

using Microsoft.UI.Xaml;

using System.Globalization;

namespace BarkFluff.Client.WinUI.Infrastructure.Localization;

/// <summary>
/// Язык выбирается один раз до создания первой Page: в WinUI нет <c>DynamicResource</c>,
/// поэтому строки берутся через <c>StaticResource</c> из словаря, домерженного на старте.
/// </summary>
public sealed class LocalizationService : ILocalizationService
{
    private const string DictionaryPrefix = "ms-appx:///Resources/Localization/Strings.";

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
            dictionary.Source?.OriginalString.StartsWith(DictionaryPrefix, StringComparison.OrdinalIgnoreCase) == true);

        if (existingDictionary is not null)
        {
            resources.Remove(existingDictionary);
        }

        resources.Add(new ResourceDictionary
        {
            Source = new Uri($"{DictionaryPrefix}{language}.xaml")
        });

        var culture = new CultureInfo(language);
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }

    public string GetString(string resourceKey) =>
        Application.Current.Resources.TryGetValue(resourceKey, out var value) && value is string text
            ? text
            : resourceKey;
}
