using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

using System.Windows.Input;

using Windows.ApplicationModel.DataTransfer;

namespace BarkFluff.Client.WinUI.Infrastructure.Behaviors;

public static class OtpInputBehavior
{
    public static readonly DependencyProperty EnableAutoAdvanceProperty = DependencyProperty.RegisterAttached(
        "EnableAutoAdvance",
        typeof(bool),
        typeof(OtpInputBehavior),
        new PropertyMetadata(false, OnEnableAutoAdvanceChanged));

    public static readonly DependencyProperty PasteCommandProperty = DependencyProperty.RegisterAttached(
        "PasteCommand",
        typeof(ICommand),
        typeof(OtpInputBehavior),
        new PropertyMetadata(null, OnPasteCommandChanged));

    public static bool GetEnableAutoAdvance(DependencyObject element) => (bool)element.GetValue(EnableAutoAdvanceProperty);

    public static void SetEnableAutoAdvance(DependencyObject element, bool value) => element.SetValue(EnableAutoAdvanceProperty, value);

    public static ICommand? GetPasteCommand(DependencyObject element) => (ICommand?)element.GetValue(PasteCommandProperty);

    public static void SetPasteCommand(DependencyObject element, ICommand? value) => element.SetValue(PasteCommandProperty, value);

    private static void OnEnableAutoAdvanceChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs eventArgs)
    {
        if (dependencyObject is not TextBox textBox)
        {
            return;
        }

        if ((bool)eventArgs.NewValue)
        {
            textBox.BeforeTextChanging += AcceptDigitsOnly;
            textBox.TextChanged += MoveToNextField;
        }
        else
        {
            textBox.BeforeTextChanging -= AcceptDigitsOnly;
            textBox.TextChanged -= MoveToNextField;
        }
    }

    private static void OnPasteCommandChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs eventArgs)
    {
        if (dependencyObject is not TextBox textBox)
        {
            return;
        }

        textBox.Paste -= PasteFromClipboard;
        if (eventArgs.NewValue is not null)
        {
            textBox.Paste += PasteFromClipboard;
        }
    }

    private static void AcceptDigitsOnly(TextBox sender, TextBoxBeforeTextChangingEventArgs eventArgs)
    {
        eventArgs.Cancel = eventArgs.NewText.Any(character => !char.IsDigit(character));
    }

    private static void MoveToNextField(object sender, TextChangedEventArgs eventArgs)
    {
        if (sender is TextBox { Text.Length: 1 })
        {
            FocusManager.TryMoveFocus(FocusNavigationDirection.Next);
        }
    }

    /// <summary>
    /// Буфер обмена в WinRT читается только асинхронно, а <c>Handled</c> обязан быть выставлен
    /// синхронно, поэтому штатная вставка отменяется всегда, и весь разбор текста ложится на
    /// команду. Для поля на один символ это не потеря: команда либо раскладывает шесть цифр
    /// по полям, либо не делает ничего.
    /// </summary>
    private static async void PasteFromClipboard(object sender, TextControlPasteEventArgs eventArgs)
    {
        if (sender is not TextBox textBox || GetPasteCommand(textBox) is not { } command)
        {
            return;
        }

        eventArgs.Handled = true;

        var clipboard = Clipboard.GetContent();
        if (!clipboard.Contains(StandardDataFormats.Text))
        {
            return;
        }

        var pasted = await clipboard.GetTextAsync();
        if (command.CanExecute(pasted))
        {
            command.Execute(pasted);
        }
    }
}
