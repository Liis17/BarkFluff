using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace BarkFluff.ClientV2.WPF.Infrastructure.Behaviors;

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
            textBox.PreviewTextInput += AcceptDigitsOnly;
            textBox.TextChanged += MoveToNextField;
        }
        else
        {
            textBox.PreviewTextInput -= AcceptDigitsOnly;
            textBox.TextChanged -= MoveToNextField;
        }
    }

    private static void OnPasteCommandChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs eventArgs)
    {
        if (dependencyObject is not TextBox textBox)
        {
            return;
        }

        DataObject.RemovePastingHandler(textBox, PasteFromClipboard);
        if (eventArgs.NewValue is not null)
        {
            DataObject.AddPastingHandler(textBox, PasteFromClipboard);
        }
    }

    private static void AcceptDigitsOnly(object sender, TextCompositionEventArgs eventArgs)
    {
        eventArgs.Handled = eventArgs.Text.Any(character => !char.IsDigit(character));
    }

    private static void MoveToNextField(object sender, TextChangedEventArgs eventArgs)
    {
        if (sender is TextBox { Text.Length: 1 } textBox)
        {
            textBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        }
    }

    private static void PasteFromClipboard(object sender, DataObjectPastingEventArgs eventArgs)
    {
        if (sender is not TextBox textBox || !eventArgs.SourceDataObject.GetDataPresent(DataFormats.UnicodeText))
        {
            return;
        }

        var pasted = eventArgs.SourceDataObject.GetData(DataFormats.UnicodeText) as string;
        var command = GetPasteCommand(textBox);
        if (pasted is not null && command?.CanExecute(pasted) is true)
        {
            command.Execute(pasted);
            eventArgs.CancelCommand();
        }
    }
}
