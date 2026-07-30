using Microsoft.UI.Xaml;

namespace BarkFluff.Client.WinUI.Infrastructure.Dialogs;

public interface IDialogService
{
    /// <summary>
    /// <c>ContentDialog</c> не умеет показываться без <see cref="XamlRoot"/>, а ViewModel
    /// не должна его знать — корень отдаёт shell сразу после создания окна.
    /// </summary>
    void Attach(XamlRoot xamlRoot);

    Task ShowMessageAsync(string title, string message, string closeTextKey);

    Task ShowContentAsync(string titleKey, object content, string closeTextKey);
}
