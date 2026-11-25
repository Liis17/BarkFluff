using BarkFluff.Client.WinUI.Views.Settings;

using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;

namespace BarkFluff.Client.WinUI.Views;

/// <summary>
/// Оболочка настроек: слева список разделов, справа <c>Frame</c> с выбранным разделом.
/// </summary>
/// <remarks>
/// Вложенный <c>Frame</c>, а не второй <c>NavigationView</c>: внешний <c>RootNavigation</c> уже владеет
/// кнопкой «назад» и бургером, и второй набор этих элементов конкурировал бы с ним. Переходы внутри
/// раздела не поднимаются до внешнего кадра, поэтому его кнопка «назад» всегда означает «выйти из
/// настроек» — именно это и требуется.
/// <para>
/// Меню чата передаёт тег раздела папок. При обычном переходе открывается раздел «Общие».
/// </para>
/// </remarks>
public sealed partial class SettingsPage : Page
{
    public SettingsPage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        SectionList.SelectedItem = SectionList.Items.OfType<ListViewItem>()
            .FirstOrDefault(item => item.Tag as string == e.Parameter as string);
        if (SectionList.SelectedIndex < 0)
        {
            SectionList.SelectedIndex = 0;
        }
    }

    private void OnSectionSelected(object sender, SelectionChangedEventArgs eventArgs)
    {
        var pageType = ((SectionList.SelectedItem as ListViewItem)?.Tag as string) switch
        {
            "general" => typeof(SettingsGeneralPage),
            "account" => typeof(SettingsAccountPage),
            "security" => typeof(SettingsSecurityPage),
            "privacy" => typeof(SettingsPrivacyPage),
            "devices" => typeof(SettingsDevicesPage),
            "personalization" => typeof(SettingsPersonalizationPage),
            "chatfolders" => typeof(SettingsChatFoldersPage),
            "notifications" => typeof(SettingsNotificationsPage),
            "language" => typeof(SettingsLanguagePage),
            "storage" => typeof(SettingsStoragePage),
            "update" => typeof(SettingsUpdatePage),
            "about" => typeof(SettingsAboutPage),
            "testing" => typeof(SettingsTestingPage),
            _ => null
        };

        if (pageType is null || SectionFrame.CurrentSourcePageType == pageType)
        {
            return;
        }

        SectionFrame.Navigate(pageType, null, new SuppressNavigationTransitionInfo());
        // Разделы переключаются списком, а не «назад», поэтому стек только мешал бы внешнему кадру.
        SectionFrame.BackStack.Clear();
    }
}
