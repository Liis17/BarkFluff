using BarkFluff.Client.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BarkFluff.Client.Core.ViewModels.Settings;

public sealed partial class SettingsLanguageViewModel(IApplicationDataStore dataStore) : ObservableObject
{
    [ObservableProperty] private string _selectedLanguage = "system";

    public async Task LoadAsync()
    {
        var language = await dataStore.GetLanguageAsync();
        SelectedLanguage = language is "ru" or "en" ? language : "system";
    }

    public async Task SelectAsync(string language)
    {
        if (language is not ("system" or "ru" or "en")) return;
        SelectedLanguage = language;
        await dataStore.SaveLanguageAsync(language);
    }
}
