using BarkFluff.Client.Core.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BarkFluff.Client.WinUI.Views;

public sealed class MessageFileTemplateSelector : DataTemplateSelector
{
    public DataTemplate FileTemplate { get; set; } = null!;
    public DataTemplate VoiceTemplate { get; set; } = null!;

    protected override DataTemplate SelectTemplateCore(object item) =>
        item is MessageAttachmentItemViewModel { IsOpusVoice: true } ? VoiceTemplate : FileTemplate;

    protected override DataTemplate SelectTemplateCore(object item, DependencyObject container) =>
        SelectTemplateCore(item);
}
