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
        if (dependencyObject is ListBox listBox && eventArgs.NewValue is MessageScrollRequest request)
        {
            listBox.Dispatcher.BeginInvoke(
                () => ApplyScrollRequest(listBox, request),
                DispatcherPriority.Loaded);
        }
    }

    private static void OnVisibleMessageCommandChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs eventArgs)
    {
        if (dependencyObject is not ListBox listBox)
        {
            return;
        }

        if (eventArgs.OldValue is not null)
        {
            listBox.Loaded -= OnListLoaded;
            listBox.Unloaded -= OnListUnloaded;
            listBox.LayoutUpdated -= OnListLayoutUpdated;
        }

        if (eventArgs.NewValue is not null)
        {
            listBox.Loaded += OnListLoaded;
            listBox.Unloaded += OnListUnloaded;
            listBox.LayoutUpdated += OnListLayoutUpdated;
        }
    }

    private static void OnListLoaded(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is ListBox listBox)
        {
            QueueVisibilityCheck(listBox);
        }
    }

    private static void OnListUnloaded(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is ListBox listBox)
        {
            listBox.SetValue(IsLayoutCheckPendingProperty, false);
        }
    }

    private static void OnListLayoutUpdated(object? sender, EventArgs eventArgs)
    {
        if (sender is ListBox listBox)
        {
            QueueVisibilityCheck(listBox);
        }
    }

    private static void QueueVisibilityCheck(ListBox listBox)
    {
        if ((bool)listBox.GetValue(IsLayoutCheckPendingProperty))
        {
            return;
        }

        listBox.SetValue(IsLayoutCheckPendingProperty, true);
        listBox.Dispatcher.BeginInvoke(
            () =>
            {
                listBox.SetValue(IsLayoutCheckPendingProperty, false);
                ReportVisibleMessages(listBox);
            },
            DispatcherPriority.Background);
    }

    private static void ReportVisibleMessages(ListBox listBox)
    {
        var command = GetVisibleMessageCommand(listBox);
        var scrollViewer = FindDescendant<ScrollViewer>(listBox);
        if (command is null || scrollViewer is null || scrollViewer.ViewportHeight <= 0)
        {
            return;
        }

        var viewport = new Rect(0, 0, scrollViewer.ViewportWidth, scrollViewer.ViewportHeight);
        for (var index = 0; index < listBox.Items.Count; index++)
        {
            if (listBox.ItemContainerGenerator.ContainerFromIndex(index) is not FrameworkElement container
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

    private static void ApplyScrollRequest(ListBox listBox, MessageScrollRequest request)
    {
        var scrollViewer = FindDescendant<ScrollViewer>(listBox);
        if (scrollViewer is null)
        {
            return;
        }

        if (request.Target == MessageScrollTarget.Bottom)
        {
            scrollViewer.ScrollToEnd();
            return;
        }

        var message = listBox.Items.OfType<MessageItemViewModel>().FirstOrDefault(item => item.Id == request.MessageId);
        if (message is null)
        {
            return;
        }

        listBox.ScrollIntoView(message);
        listBox.Dispatcher.BeginInvoke(
            () => CenterMessage(listBox, scrollViewer, message),
            DispatcherPriority.ContextIdle);
    }

    private static void CenterMessage(ListBox listBox, ScrollViewer scrollViewer, MessageItemViewModel message)
    {
        if (listBox.ItemContainerGenerator.ContainerFromItem(message) is not FrameworkElement container)
        {
            return;
        }

        var bounds = container.TransformToAncestor(scrollViewer)
            .TransformBounds(new Rect(0, 0, container.ActualWidth, container.ActualHeight));
        scrollViewer.ScrollToVerticalOffset(Math.Max(0, scrollViewer.VerticalOffset + bounds.Top - (scrollViewer.ViewportHeight - bounds.Height) / 2));
    }

    private static T? FindDescendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T typed)
            {
                return typed;
            }

            var descendant = FindDescendant<T>(child);
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
    }
}
