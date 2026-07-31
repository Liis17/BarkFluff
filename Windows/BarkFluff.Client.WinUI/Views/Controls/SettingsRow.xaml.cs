using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace BarkFluff.Client.WinUI.Views.Controls;

/// <summary>
/// Строка настроек: иконка, заголовок с пояснением, бейдж недоступности и правый слот под управляющий
/// элемент. Написана вручную, потому что <c>CommunityToolkit.WinUI.Controls.SettingsControls</c>
/// выходит только в preview и собран против WindowsAppSDK 1.6, тогда как проект на 2.3.1: расхождение
/// проявилось бы в рантайме, а рантайм в этом репозитории проверить нечем.
/// </summary>
/// <remarks>
/// Слот действия объявлен свойством содержимого, поэтому в разметке пишется дочерним элементом:
/// <c>&lt;SettingsRow Header="…"&gt;&lt;ToggleSwitch IsOn="{x:Bind …}" /&gt;&lt;/SettingsRow&gt;</c>.
/// Такое содержимое компилируется в namescope страницы-потребителя, поэтому <c>x:Bind</c> в нём работает.
/// </remarks>
[ContentProperty(Name = nameof(ActionContent))]
public sealed partial class SettingsRow : UserControl
{
    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph), typeof(string), typeof(SettingsRow), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(
        nameof(Header), typeof(string), typeof(SettingsRow), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description), typeof(string), typeof(SettingsRow), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty BadgeTextProperty = DependencyProperty.Register(
        nameof(BadgeText), typeof(string), typeof(SettingsRow), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ActionContentProperty = DependencyProperty.Register(
        nameof(ActionContent), typeof(object), typeof(SettingsRow), new PropertyMetadata(null));

    public SettingsRow() => InitializeComponent();

    /// <summary>Глиф Segoe Fluent Icons. Пустая строка убирает колонку иконки.</summary>
    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    public string Header
    {
        get => (string)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    /// <summary>Пояснение под заголовком. Пустая строка прячет вторую строку.</summary>
    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    /// <summary>Причина недоступности пункта. Пустая строка прячет бейдж.</summary>
    public string BadgeText
    {
        get => (string)GetValue(BadgeTextProperty);
        set => SetValue(BadgeTextProperty, value);
    }

    public object? ActionContent
    {
        get => GetValue(ActionContentProperty);
        set => SetValue(ActionContentProperty, value);
    }
}
