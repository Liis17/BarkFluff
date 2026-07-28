using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace BarkFluff.ClientV2.WPF.Infrastructure.Behaviors;

public static class SendOnEnterBehavior
{
    public static readonly DependencyProperty CommandProperty = DependencyProperty.RegisterAttached(
        "Command", typeof(ICommand), typeof(SendOnEnterBehavior), new PropertyMetadata(null, OnCommandChanged));

    public static void SetCommand(DependencyObject element, ICommand? value) => element.SetValue(CommandProperty, value);
    public static ICommand? GetCommand(DependencyObject element) => (ICommand?)element.GetValue(CommandProperty);

    private static void OnCommandChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs eventArgs)
    {
        if (dependencyObject is not TextBox textBox)
        {
            return;
        }

        textBox.PreviewKeyDown -= OnPreviewKeyDown;
        if (eventArgs.NewValue is not null)
        {
            textBox.PreviewKeyDown += OnPreviewKeyDown;
        }
    }

    private static void OnPreviewKeyDown(object sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key != Key.Enter || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) || sender is not TextBox textBox)
        {
            return;
        }

        var command = GetCommand(textBox);
        if (command?.CanExecute(null) == true)
        {
            command.Execute(null);
            eventArgs.Handled = true;
        }
    }
}
