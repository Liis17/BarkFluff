using BarkFluff.Client.Core.ViewModels;

namespace BarkFluff.Client.WinUI.Views;

/// <summary>
/// Неявных <c>DataTemplate</c> по <c>DataType</c> в WinUI нет, поэтому соответствие
/// ViewModel → Page задаётся здесь, в shell-слое. Сервис навигации о типах Page не знает.
/// </summary>
public static class ViewLocator
{
    private static readonly Dictionary<Type, Type> Pages = new()
    {
        [typeof(WelcomeViewModel)] = typeof(WelcomePage),
        [typeof(SelectNodeViewModel)] = typeof(SelectNodePage),
        [typeof(ConnectedNodeViewModel)] = typeof(ConnectedNodePage),
        [typeof(LoginViewModel)] = typeof(LoginPage),
        [typeof(RegistrationViewModel)] = typeof(RegistrationPage),
        [typeof(PasswordRecoveryViewModel)] = typeof(PasswordRecoveryPage),
        [typeof(MessengerViewModel)] = typeof(MessengerPage)
    };

    public static Type? Resolve(object? viewModel) =>
        viewModel is not null && Pages.TryGetValue(viewModel.GetType(), out var pageType)
            ? pageType
            : null;
}
