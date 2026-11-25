using BarkFluff.Client.Core.Infrastructure.Localization;
using BarkFluff.Client.Core.ViewModels;
using BarkFluff.Proto.Calls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;

namespace BarkFluff.Client.WinUI.Views;

public sealed partial class MessengerPage
{
    private async void OnCallHistoryClick(object sender, RoutedEventArgs args)
    {
        if (ViewModel.OpenCallHistory() is not { } history)
            return;

        var localization = App.Services.GetRequiredService<ILocalizationService>();
        var filter = new ComboBox { Header = localization.GetString("Calls_History") };
        AutomationProperties.SetName(filter, localization.GetString("Calls_HistoryFilter"));
        filter.Items.Add(localization.GetString("Calls_HistoryAll"));
        filter.Items.Add(localization.GetString("Calls_HistoryMissedFilter"));
        filter.SelectedIndex = 0;
        var list = new ListView
        {
            ItemsSource = history.Items,
            ItemTemplate = (DataTemplate)Resources["CallHistoryEntryTemplate"],
            MaxHeight = 320,
            SelectionMode = ListViewSelectionMode.Single
        };
        AutomationProperties.SetName(list, localization.GetString("Calls_History"));
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(filter);
        content.Children.Add(status);
        content.Children.Add(list);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = localization.GetString("Calls_History"),
            Content = content,
            PrimaryButtonText = localization.GetString("Calls_HistoryMore"),
            SecondaryButtonText = localization.GetString("Calls_CallBack"),
            CloseButtonText = localization.GetString("Calls_Dismiss"),
            IsSecondaryButtonEnabled = false,
            IsPrimaryButtonEnabled = false
        };

        void Render()
        {
            if (!history.IsOpen)
            {
                dialog.Hide();
                return;
            }
            dialog.IsPrimaryButtonEnabled = history.HasMore && !history.IsLoading;
            status.Text = history.Error is { } key ? localization.GetString(key)
                : history.IsLoading ? localization.GetString("Calls_HistoryLoading")
                : history.Items.Count == 0 ? localization.GetString("Calls_HistoryEmpty") : string.Empty;
            status.Visibility = status.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        list.SelectionChanged += (_, _) => dialog.IsSecondaryButtonEnabled = list.SelectedItem is CallHistoryEntry;
        filter.SelectionChanged += async (_, _) => await history.SetFilterAsync(filter.SelectedIndex == 1
            ? CallHistoryFilter.CallHistoryMissed : CallHistoryFilter.CallHistoryAll);
        dialog.PrimaryButtonClick += async (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            await history.LoadMoreAsync();
        };
        dialog.Opened += async (_, _) => await history.LoadMoreAsync();
        history.Changed += Render;
        try
        {
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Secondary && list.SelectedItem is CallHistoryEntry entry)
                await ViewModel.CallBackAsync(history, entry);
        }
        finally
        {
            history.Changed -= Render;
            ViewModel.CloseCallHistory(history);
        }
    }
}
