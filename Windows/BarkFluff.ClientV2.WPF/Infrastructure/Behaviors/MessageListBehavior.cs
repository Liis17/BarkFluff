using BarkFluff.ClientV2.WPF.Models;
using BarkFluff.ClientV2.WPF.ViewModels;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace BarkFluff.ClientV2.WPF.Infrastructure.Behaviors;

public static class MessageListBehavior
{
    public static readonly DependencyProperty ScrollRequestProperty = DependencyProperty.RegisterAttached(
        "ScrollRequest",
        typeof(MessageScrollRequest),
        typeof(MessageListBehavior),
        new PropertyMetadata(null, OnScrollRequestChanged));

    public static readonly DependencyProperty VisibleMessageCommandProperty = DependencyProperty.RegisterAttached(
        "VisibleMessageCommand",
        typeof(ICommand),
        typeof(MessageListBehavior),
        new PropertyMetadata(null, OnVisibleMessageCommandChanged));

    private static readonly DependencyProperty IsLayoutCheckPendingProperty = DependencyProperty.RegisterAttached(
        "IsLayoutCheckPending",
        typeof(bool),
        typeof(MessageListBehavior));

    public static MessageScrollRequest? GetScrollRequest(DependencyObject element) =>
        (MessageScrollRequest?)element.GetValue(ScrollRequestProperty);

    public static void SetScrollRequest(DependencyObject element, MessageScrollRequest? value) =>
        element.SetValue(ScrollRequestProperty, value);

    public static ICommand? GetVisibleMessageCommand(DependencyObject element) =>
        (ICommand?)element.GetValue(VisibleMessageCommandProperty);

    public static void SetVisibleMessageCommand(DependencyObject element, ICommand? value) =>
        element.SetValue(VisibleMessageCommandProperty, value);

    private static void OnScrollRequestChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs eventArgs)
    {
        if (dependencyObject is ItemsControl itemsControl && eventArgs.NewValue is MessageScrollRequest request)
        {
            itemsControl.Dispatcher.BeginInvoke(
                () => ApplyScrollRequest(itemsControl, request),
                DispatcherPriority.Loaded);
        }
    }

    private static void OnVisibleMessageCommandChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs eventArgs)
    {
        if (dependencyObject is not ItemsControl itemsControl)
        {
            return;
        }

        if (eventArgs.OldValue is not null)
        {
            itemsControl.Loaded -= OnListLoaded;
            itemsControl.Unloaded -= OnListUnloaded;
            itemsControl.LayoutUpdated -= OnListLayoutUpdated;
        }

        if (eventArgs.NewValue is not null)
        {
            itemsControl.Loaded += OnListLoaded;
            itemsControl.Unloaded += OnListUnloaded;
            itemsControl.LayoutUpdated += OnListLayoutUpdated;
        }
    }

    private static void OnListLoaded(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is ItemsControl itemsControl)
        {
            QueueVisibilityCheck(itemsControl);
        }
    }

    private static void OnListUnloaded(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is ItemsControl itemsControl)
        {
            itemsControl.SetValue(IsLayoutCheckPendingProperty, false);
        }
    }

    private static void OnListLayoutUpdated(object? sender, EventArgs eventArgs)
    {
        if (sender is ItemsControl itemsControl)
        {
            QueueVisibilityCheck(itemsControl);
        }
    }

    private static void QueueVisibilityCheck(ItemsControl itemsControl)
    {
        if ((bool)itemsControl.GetValue(IsLayoutCheckPendingProperty))
        {
            return;
        }

        itemsControl.SetValue(IsLayoutCheckPendingProperty, true);
        itemsControl.Dispatcher.BeginInvoke(
            () =>
            {
                itemsControl.SetValue(IsLayoutCheckPendingProperty, false);
                ReportVisibleMessages(itemsControl);
            },
            DispatcherPriority.Background);
    }

    private static void ReportVisibleMessages(ItemsControl itemsControl)
    {
        var command = GetVisibleMessageCommand(itemsControl);
        var scrollViewer = FindAncestor<ScrollViewer>(itemsControl);
        if (command is null || scrollViewer is null || scrollViewer.ViewportHeight <= 0)
        {
            return;
        }

        var viewport = new Rect(0, 0, scrollViewer.ViewportWidth, scrollViewer.ViewportHeight);
        for (var index = 0; index < itemsControl.Items.Count; index++)
        {
            if (itemsControl.ItemContainerGenerator.ContainerFromIndex(index) is not FrameworkElement container
                || container.DataContext is not MessageItemViewModel message
                || container.ActualHeight <= 0)
            {
                continue;
            }

            var bounds = container.TransformToAncestor(scrollViewer)
                .TransformBounds(new Rect(0, 0, container.ActualWidth, container.ActualHeight));
            var visibleBounds = Rect.Intersect(bounds, viewport);
            if (visibleBounds.Height / bounds.Height >= 0.5 && command.CanExecute(message))
            {
                command.Execute(message);
            }
        }
    }

    private static void ApplyScrollRequest(ItemsControl itemsControl, MessageScrollRequest request)
    {
        var scrollViewer = FindAncestor<ScrollViewer>(itemsControl);
        if (scrollViewer is null)
        {
            return;
        }

        if (request.Target == MessageScrollTarget.Bottom)
        {
            scrollViewer.ScrollToEnd();
            return;
        }

        var message = itemsControl.Items.OfType<MessageItemViewModel>().FirstOrDefault(item => item.Id == request.MessageId);
        if (message is null)
        {
            return;
        }

        // Контейнеры не виртуализируются, так что достаточно одного прохода после раскладки.
        itemsControl.Dispatcher.BeginInvoke(
            () => CenterMessage(itemsControl, scrollViewer, message),
            DispatcherPriority.ContextIdle);
    }

    private static void CenterMessage(ItemsControl itemsControl, ScrollViewer scrollViewer, MessageItemViewModel message)
    {
        if (itemsControl.ItemContainerGenerator.ContainerFromItem(message) is not FrameworkElement container)
        {
            return;
        }

        var bounds = container.TransformToAncestor(scrollViewer)
            .TransformBounds(new Rect(0, 0, container.ActualWidth, container.ActualHeight));
        scrollViewer.ScrollToVerticalOffset(Math.Max(0, scrollViewer.VerticalOffset + bounds.Top - (scrollViewer.ViewportHeight - bounds.Height) / 2));
    }

    private static T? FindAncestor<T>(DependencyObject element)
        where T : DependencyObject
    {
        for (var parent = VisualTreeHelper.GetParent(element); parent is not null; parent = VisualTreeHelper.GetParent(parent))
        {
            if (parent is T typed)
            {
                return typed;
            }
        }

        return null;
    }
}
