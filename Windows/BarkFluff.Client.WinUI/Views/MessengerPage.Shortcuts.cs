using BarkFluff.Client.Core.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace BarkFluff.Client.WinUI.Views;

public sealed partial class MessengerPage
{
    private bool _isComposingText;

    private void InitializeShortcuts()
    {
        ComposerBox.TextCompositionStarted += (_, _) => _isComposingText = true;
        ComposerBox.TextCompositionEnded += (_, _) => _isComposingText = false;
        ComposerBox.LostFocus += (_, _) => _isComposingText = false;

        Register(ShortcutScope, VirtualKey.K, VirtualKeyModifiers.Control, MessengerShortcutKey.CtrlK);
        Register(ShortcutScope, VirtualKey.F, VirtualKeyModifiers.Control, MessengerShortcutKey.CtrlF);
        Register(ShortcutScope, VirtualKey.Escape, VirtualKeyModifiers.None, MessengerShortcutKey.Escape);
        Register(ShortcutScope, VirtualKey.Up, VirtualKeyModifiers.Control, MessengerShortcutKey.CtrlUp);
        Register(ShortcutScope, VirtualKey.Down, VirtualKeyModifiers.Control, MessengerShortcutKey.CtrlDown);

        // На поле ввода акселератор обрабатывается до KeyDown самого TextBox.
        Register(ComposerBox, VirtualKey.Up, VirtualKeyModifiers.None, MessengerShortcutKey.Up);
        Register(ComposerBox, VirtualKey.Up, VirtualKeyModifiers.Control, MessengerShortcutKey.CtrlUp);
        Register(ComposerBox, VirtualKey.Down, VirtualKeyModifiers.Control, MessengerShortcutKey.CtrlDown);
        Register(ComposerBox, VirtualKey.Escape, VirtualKeyModifiers.None, MessengerShortcutKey.Escape);

        void Register(UIElement owner, VirtualKey key, VirtualKeyModifiers modifiers, MessengerShortcutKey shortcut)
        {
            owner.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;
            var accelerator = new KeyboardAccelerator { Key = key, Modifiers = modifiers, ScopeOwner = owner };
            accelerator.Invoked += (_, args) =>
            {
                if (ViewModel is not null && XamlRoot is { } root)
                    args.Handled = ViewModel.TryInvokeShortcut(shortcut, GetShortcutFocus(root, shortcut),
                        () => SidebarSearchBox.Focus(FocusState.Keyboard));
            };
            owner.KeyboardAccelerators.Add(accelerator);
        }
    }

    private MessengerShortcutFocus GetShortcutFocus(XamlRoot root, MessengerShortcutKey shortcut)
    {
        // Esc для flyout и ContentDialog обрабатывает WinUI.
        if (VisualTreeHelper.GetOpenPopupsForXamlRoot(root).Any(popup => popup.IsLightDismissEnabled))
            return MessengerShortcutFocus.Protected;

        var focus = MessengerShortcutFocus.Other;
        for (var element = FocusManager.GetFocusedElement(root) as DependencyObject; element is not null;
             element = VisualTreeHelper.GetParent(element))
        {
            if (element == ShortcutScope)
                return focus;
            if (element is ContentDialog)
                return MessengerShortcutFocus.Protected;
            if (shortcut == MessengerShortcutKey.Escape &&
                (element == PrivateUnlockOverlay || element == DeleteConfirmOverlay || element == ForwardOverlay ||
                 element == ProfileOverlay || element == SecretChatsOverlay))
                return MessengerShortcutFocus.Other;
            if (element == ComposerBox)
                focus = _isComposingText ? MessengerShortcutFocus.Protected : MessengerShortcutFocus.Composer;
            else if (element is TextBox or RichEditBox or PasswordBox or ComboBox)
                focus = MessengerShortcutFocus.Protected;
        }
        return MessengerShortcutFocus.Protected;
    }
}
