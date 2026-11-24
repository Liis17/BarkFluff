using BarkFluff.Client.Core.ViewModels;
using BarkFluff.Proto.Messages;
using BarkFluff.Proto.Shared;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace BarkFluff.Client.WinUI.Views;

public sealed partial class MessengerPage
{
    private static readonly (MessageAttachmentType Type, string Label, string Glyph)[] SearchAttachmentOptions =
    [
        (MessageAttachmentType.Image, "Messenger_AttachmentPhoto", "\uEB9F"),
        (MessageAttachmentType.Video, "Messenger_AttachmentVideo", "\uE8B2"),
        (MessageAttachmentType.Gif, "Messenger_AttachmentGif", "\uE8B9"),
        (MessageAttachmentType.Document, "Messenger_AttachmentFile", "\uE8A5"),
        (MessageAttachmentType.Audio, "Messenger_AttachmentAudio", "\uE8D6"),
        (MessageAttachmentType.Voice, "Messenger_VoiceAttachment", "\uE720"),
        (MessageAttachmentType.Sticker, "Messenger_SearchSticker", "\uE899")
    ];

    private void OnChatSearchResultClick(object sender, RoutedEventArgs args)
    {
        if (sender is Button { Tag: ChatItemViewModel chat })
            ViewModel.OpenChatSearchResult(chat);
    }

    private async void OnPeopleSearchResultClick(object sender, RoutedEventArgs args)
    {
        if (sender is Button { Tag: UserSearchResultViewModel result })
            await ViewModel.OpenSearchResultAsync(result);
    }

    private async void OnMessageSearchResultClick(object sender, RoutedEventArgs args)
    {
        if (sender is Button { Tag: MessageSearchResultViewModel result })
            await ViewModel.OpenMessageSearchResultAsync(result);
    }

    private void OnSearchAttachmentIconsLoaded(object sender, RoutedEventArgs args)
    {
        var panel = (StackPanel)sender;
        panel.Children.Clear();
        if (panel.Tag is not IReadOnlyList<MessageAttachmentType> types)
            return;

        foreach (var option in SearchAttachmentOptions.Where(option => types.Contains(option.Type)))
        {
            var icon = new FontIcon { Glyph = option.Glyph, FontSize = 14 };
            var name = SearchLabel(option.Label);
            AutomationProperties.SetName(icon, name);
            ToolTipService.SetToolTip(icon, name);
            panel.Children.Add(icon);
        }
    }

    private void OnSearchFiltersOpening(object sender, object args)
    {
        var selected = ViewModel.SearchAuthor;
        var options = new List<ComboBoxItem> { new() { Content = SearchLabel("Messenger_SearchAnyAuthor") } };
        options.AddRange(ViewModel.GetSearchAuthors().Select(author => new ComboBoxItem
        {
            Tag = author,
            Content = string.IsNullOrWhiteSpace(author.DisplayName)
                ? SearchLabel("Messenger_SearchUnknownAuthor") : author.DisplayName
        }));
        SearchAuthorPicker.ItemsSource = options;
        SearchAuthorPicker.SelectedItem = options.FirstOrDefault(option => Equals(option.Tag, selected)) ?? options[0];
        SearchFromPicker.Date = ViewModel.SearchFrom;
        SearchThroughPicker.Date = ViewModel.SearchThrough;
        SearchAttachmentPicker.ItemsSource = SearchAttachmentOptions.Select(option => new CheckBox
        {
            Content = SearchLabel(option.Label),
            Tag = option.Type,
            IsChecked = ViewModel.SearchAttachmentTypes.Contains(option.Type)
        }).ToArray();
        SearchPresencePicker.SelectedIndex = (int)ViewModel.SearchAttachmentPresence;
        UpdateSearchAttachmentPicker();
    }

    private void OnSearchPresenceChanged(object sender, SelectionChangedEventArgs args) => UpdateSearchAttachmentPicker();

    private void UpdateSearchAttachmentPicker()
    {
        if (SearchAttachmentPicker?.ItemsSource is not CheckBox[] checks)
            return;

        var enabled = SearchPresencePicker.SelectedIndex != (int)MessageSearchAttachmentPresence.Without;
        foreach (var check in checks)
            check.IsEnabled = enabled;
    }

    private void OnSearchFiltersApply(object sender, RoutedEventArgs args)
    {
        var author = (SearchAuthorPicker.SelectedItem as ComboBoxItem)?.Tag as MessageSearchAuthor;
        var types = ((CheckBox[])SearchAttachmentPicker.ItemsSource).Where(check => check.IsEnabled && check.IsChecked == true)
            .Select(check => (MessageAttachmentType)check.Tag).ToArray();
        ViewModel.ApplyMessageSearchFilters(author, SearchFromPicker.Date, SearchThroughPicker.Date,
            (MessageSearchAttachmentPresence)SearchPresencePicker.SelectedIndex, types);
        SearchFiltersFlyout.Hide();
    }

    private void OnSearchFiltersReset(object sender, RoutedEventArgs args)
    {
        ViewModel.ApplyMessageSearchFilters(null, null, null, MessageSearchAttachmentPresence.Any, []);
        SearchFiltersFlyout.Hide();
    }

    private static string SearchLabel(string key) => (string)Application.Current.Resources[key];
}
