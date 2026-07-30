using BarkFluff.Client.Core.Infrastructure.Localization;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BarkFluff.Client.WinUI.Infrastructure.Dialogs;

public sealed class DialogService : IDialogService
{
    private readonly ILocalizationService _localization;

    private XamlRoot? _xamlRoot;

    public DialogService(ILocalizationService localization)
    {
        _localization = localization;
    }

    public void Attach(XamlRoot xamlRoot) => _xamlRoot = xamlRoot;

    public Task ShowMessageAsync(string title, string message, string closeTextKey) =>
        ShowAsync(title, new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }, closeTextKey);

    public Task ShowContentAsync(string titleKey, object content, string closeTextKey) =>
        ShowAsync(_localization.GetString(titleKey), content, closeTextKey);

    private async Task ShowAsync(string title, object content, string closeTextKey)
    {
        if (_xamlRoot is null)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = _xamlRoot,
            Title = title,
            Content = content,
            CloseButtonText = _localization.GetString(closeTextKey)
        };

        await dialog.ShowAsync();
    }
}
