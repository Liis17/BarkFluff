using BarkFluff.Client.Core.ViewModels;

using Microsoft.Extensions.DependencyInjection;

using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace BarkFluff.Client.WinUI.Views;

public sealed partial class ProfilePage : Page
{
    public ProfilePage() => InitializeComponent();

    public ProfileViewModel ViewModel { get; private set; } = null!;

    /// <summary>
    /// Единственная страница, на которую переходят из другой страницы (из заголовка чата),
    /// поэтому параметром приезжает идентификатор пользователя, а ViewModel берётся из контейнера:
    /// у <c>Page</c> своего доступа к DI нет, а тащить её через два уровня навигации незачем.
    /// </summary>
    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel = App.Services.GetRequiredService<ProfileViewModel>();
        Bindings.Update();
        await ViewModel.LoadAsync(e.Parameter as long? ?? 0);
    }
}
