using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace BarkFluff.ClientV2.WPF.Infrastructure.Behaviors;

public static class AvatarImageBehavior
{
    public static readonly DependencyProperty InitialsElementProperty = DependencyProperty.RegisterAttached(
        "InitialsElement",
        typeof(UIElement),
        typeof(AvatarImageBehavior),
        new PropertyMetadata(null, OnInitialsElementChanged));

    public static UIElement? GetInitialsElement(DependencyObject element) =>
        (UIElement?)element.GetValue(InitialsElementProperty);

    public static void SetInitialsElement(DependencyObject element, UIElement? value) =>
        element.SetValue(InitialsElementProperty, value);

    private static void OnInitialsElementChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs eventArgs)
    {
        if (dependencyObject is not Image image)
        {
            return;
        }

        image.TargetUpdated -= OnImageTargetUpdated;
        image.ImageFailed -= OnImageFailed;
        if (eventArgs.NewValue is not null)
        {
            image.TargetUpdated += OnImageTargetUpdated;
            image.ImageFailed += OnImageFailed;
            UpdateInitialsVisibility(image);
        }
    }

    private static void OnImageTargetUpdated(object? sender, DataTransferEventArgs eventArgs)
    {
        if (sender is Image image)
        {
            UpdateInitialsVisibility(image);
        }
    }

    private static void OnImageFailed(object? sender, ExceptionRoutedEventArgs eventArgs)
    {
        if (sender is Image image && GetInitialsElement(image) is { } initials)
        {
            initials.Visibility = Visibility.Visible;
        }
    }

    private static void UpdateInitialsVisibility(Image image)
    {
        if (GetInitialsElement(image) is { } initials)
        {
            initials.Visibility = image.Source is null ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
