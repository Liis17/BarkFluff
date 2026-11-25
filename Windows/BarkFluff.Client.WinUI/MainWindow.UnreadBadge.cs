using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using BarkFluff.Client.Core.Infrastructure.Localization;
using BarkFluff.Client.Core.ViewModels;
using BarkFluff.Client.Core.ViewModels.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.Windows.BadgeNotifications;
using Windows.System.UserProfile;

namespace BarkFluff.Client.WinUI;

public sealed partial class MainWindow
{
    private MessengerViewModel? _unreadBadgeSource;
    private uint? _taskbarBadgeCount;
    private bool _taskbarBadgeUnavailable;
    private ResourceDictionary? _trayLanguageResources;
    private CultureInfo? _trayCulture;

    private void WatchUnreadBadge(MessengerViewModel? messenger)
    {
        if (ReferenceEquals(_unreadBadgeSource, messenger) && (_taskbarBadgeCount.HasValue || _taskbarBadgeUnavailable))
            return;

        if (_unreadBadgeSource is not null)
            _unreadBadgeSource.PropertyChanged -= OnUnreadBadgeChanged;
        _unreadBadgeSource = messenger;
        if (messenger is not null)
            messenger.PropertyChanged += OnUnreadBadgeChanged;
        RenderUnreadBadge();
    }

    private void OnUnreadBadgeChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(MessengerViewModel.UnreadBadgeCount))
            RenderUnreadBadge();
    }

    private void RenderUnreadBadge()
    {
        var count = _unreadBadgeSource?.UnreadBadgeCount ?? 0;
        RenderTrayTooltip(count);

        var taskbarCount = (uint)Math.Min(count, 100);
        if (_taskbarBadgeUnavailable || _taskbarBadgeCount == taskbarCount)
            return;

        try
        {
            SetTaskbarBadge(taskbarCount);
            _taskbarBadgeCount = taskbarCount;
        }
        catch (Exception exception) when (exception is COMException or InvalidOperationException
            or UnauthorizedAccessException or TypeLoadException or DllNotFoundException)
        {
            _taskbarBadgeUnavailable = true;
            Debug.WriteLine($"Не удалось обновить бейдж панели задач (HRESULT 0x{exception.HResult:X8}).");
        }
    }

    // Загрузка типов API должна происходить внутри защищённого вызова.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void SetTaskbarBadge(uint count)
    {
        var badge = BadgeNotificationManager.Current;
        if (count == 0)
            badge.ClearBadge();
        else
            badge.SetBadgeAsCount(count);
    }

    private void OnTrayLanguageChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(SettingsLanguageViewModel.SelectedLanguage))
            return;

        var language = _languageSettings.SelectedLanguage;
        if (language == "system")
        {
            var preferred = GlobalizationPreferences.Languages.FirstOrDefault();
            language = preferred?.Split('-')[0].Equals("ru", StringComparison.OrdinalIgnoreCase) == true ? "ru" : "en";
        }

        _trayCulture = CultureInfo.GetCultureInfo(language);
        _trayLanguageResources = language == CultureInfo.CurrentUICulture.TwoLetterISOLanguageName
            ? null
            : new ResourceDictionary { Source = new Uri($"ms-appx:///Resources/Localization/Strings.{language}.xaml") };
        RenderTrayTooltip(_unreadBadgeSource?.UnreadBadgeCount ?? 0);
    }

    private void RenderTrayTooltip(long count)
    {
        var key = count == 0 ? "App_Title" : "Tray_UnreadMessages";
        var text = _trayLanguageResources?[key] as string
            ?? App.Services.GetRequiredService<ILocalizationService>().GetString(key);
        TrayIcon.ToolTipText = count == 0 ? text : string.Format(_trayCulture ?? CultureInfo.CurrentCulture, text, count);
    }
}
