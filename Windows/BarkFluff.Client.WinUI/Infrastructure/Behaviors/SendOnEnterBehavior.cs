using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

using System.Windows.Input;

using Windows.System;
using Windows.UI.Core;

namespace BarkFluff.Client.WinUI.Infrastructure.Behaviors;

public static class SendOnEnterBehavior
{
    public static readonly DependencyProperty CommandProperty = DependencyProperty.RegisterAttached(
        "Command",
        typeof(ICommand),
        typeof(SendOnEnterBehavior),
        new PropertyMetadata(null, OnCommandChanged));

    public static ICommand? GetCommand(DependencyObject element) => (ICommand?)element.GetValue(CommandProperty);

    public static void SetCommand(DependencyObject element, ICommand? value) => element.SetValue(CommandProperty, value);

    private static void OnCommandChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs eventArgs)
    {
        if (dependencyObject is not TextBox textBox)
        {
            return;
        }

        textBox.KeyDown -= OnKeyDown;
        if (eventArgs.NewValue is not null)
        {
            textBox.KeyDown += OnKeyDown;
        }
    }

    /// <summary>
    /// Shift+Enter оставляет перенос строки. В WinUI нет <c>Keyboard.Modifiers</c>,
    /// состояние клавиши приходится спрашивать у источника ввода потока.
    /// </summary>
    private static void OnKeyDown(object sender, KeyRoutedEventArgs eventArgs)
    {
        if (eventArgs.Key != VirtualKey.Enter || sender is not TextBox textBox)
        {
            return;
        }

        var shift = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift);
        if (shift.HasFlag(CoreVirtualKeyStates.Down))
        {
            return;
        }

        if (GetCommand(textBox) is { } command && command.CanExecute(null))
        {
            command.Execute(null);
            eventArgs.Handled = true;
        }
    }
}
