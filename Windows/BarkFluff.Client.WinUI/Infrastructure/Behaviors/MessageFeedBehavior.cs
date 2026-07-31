using BarkFluff.Client.Core.Models;
using BarkFluff.Client.Core.ViewModels;

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

using System.Windows.Input;

using Windows.Foundation;

namespace BarkFluff.Client.WinUI.Infrastructure.Behaviors;

/// <summary>
/// Прокрутка ленты и отчёт о её положении. Свойства вешаются на <see cref="ItemsRepeater"/>,
/// сам <see cref="ScrollViewer"/> передаётся ссылкой: он лежит рядом с именем в разметке,
/// и поиск по визуальному дереву тут был бы лишним источником отказов.
/// </summary>
public static class MessageFeedBehavior
{
    /// <summary>Насколько близко к нижней кромке лента ещё считается «в конце», в пикселях.</summary>
    private const double NearBottomThreshold = 48;

    /// <summary>Доля видимой площади, с которой сообщение считается прочитанным.</summary>
    private const double VisibleRatio = 0.5;

    public static readonly DependencyProperty ScrollerProperty = DependencyProperty.RegisterAttached(
        "Scroller",
        typeof(ScrollViewer),
        typeof(MessageFeedBehavior),
        new PropertyMetadata(null, OnScrollerChanged));

    /// <summary>Обратная ссылка на ленту, чтобы обработчик прокрутки не искал её по дереву.</summary>
    private static readonly DependencyProperty OwnerProperty = DependencyProperty.RegisterAttached(
        "Owner",
        typeof(ItemsRepeater),
        typeof(MessageFeedBehavior),
        new PropertyMetadata(null));

    public static readonly DependencyProperty ScrollRequestProperty = DependencyProperty.RegisterAttached(
        "ScrollRequest",
        typeof(MessageScrollRequest),
        typeof(MessageFeedBehavior),
        new PropertyMetadata(null, OnScrollRequestChanged));

    public static readonly DependencyProperty VisibleMessageCommandProperty = DependencyProperty.RegisterAttached(
        "VisibleMessageCommand",
        typeof(ICommand),
        typeof(MessageFeedBehavior),
        new PropertyMetadata(null));

    public static readonly DependencyProperty FeedPositionCommandProperty = DependencyProperty.RegisterAttached(
        "FeedPositionCommand",
        typeof(ICommand),
        typeof(MessageFeedBehavior),
        new PropertyMetadata(null));

    public static ScrollViewer? GetScroller(DependencyObject element) => (ScrollViewer?)element.GetValue(ScrollerProperty);

    public static void SetScroller(DependencyObject element, ScrollViewer? value) => element.SetValue(ScrollerProperty, value);

    public static MessageScrollRequest? GetScrollRequest(DependencyObject element) => (MessageScrollRequest?)element.GetValue(ScrollRequestProperty);

    public static void SetScrollRequest(DependencyObject element, MessageScrollRequest? value) => element.SetValue(ScrollRequestProperty, value);

    public static ICommand? GetVisibleMessageCommand(DependencyObject element) => (ICommand?)element.GetValue(VisibleMessageCommandProperty);

    public static void SetVisibleMessageCommand(DependencyObject element, ICommand? value) => element.SetValue(VisibleMessageCommandProperty, value);

    public static ICommand? GetFeedPositionCommand(DependencyObject element) => (ICommand?)element.GetValue(FeedPositionCommandProperty);

    public static void SetFeedPositionCommand(DependencyObject element, ICommand? value) => element.SetValue(FeedPositionCommandProperty, value);

    private static void OnScrollerChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs eventArgs)
    {
        if (dependencyObject is not ItemsRepeater repeater)
        {
            return;
        }

        if (eventArgs.OldValue is ScrollViewer previous)
        {
            previous.ViewChanged -= OnViewChanged;
            previous.SetValue(OwnerProperty, null);
            repeater.ElementPrepared -= OnElementPrepared;
            repeater.ElementClearing -= OnElementClearing;
        }

        if (eventArgs.NewValue is ScrollViewer scroller)
        {
            scroller.SetValue(OwnerProperty, repeater);
            scroller.ViewChanged += OnViewChanged;
            repeater.ElementPrepared += OnElementPrepared;
            repeater.ElementClearing += OnElementClearing;
        }
    }

    /// <summary>
    /// Положение отслеживается непрерывно, а не в момент запроса прокрутки: к тому моменту
    /// новое сообщение уже добавлено в коллекцию, и лента формально перестала быть «в конце».
    /// </summary>
    private static void OnViewChanged(object? sender, ScrollViewerViewChangedEventArgs eventArgs)
    {
        if (sender is not ScrollViewer scroller || scroller.GetValue(OwnerProperty) is not ItemsRepeater repeater)
        {
            return;
        }

        var isAtBottom = scroller.ScrollableHeight - scroller.VerticalOffset <= NearBottomThreshold;
        if (GetFeedPositionCommand(repeater) is { } command && command.CanExecute(isAtBottom))
        {
            command.Execute(isAtBottom);
        }
    }

    private static void OnScrollRequestChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs eventArgs)
    {
        if (dependencyObject is not ItemsRepeater repeater
            || eventArgs.NewValue is not MessageScrollRequest request
            || GetScroller(repeater) is not { } scroller)
        {
            return;
        }

        // Коллекция изменилась только что — раскладка ещё не прошла, измерять пока нечего.
        repeater.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => Apply(repeater, scroller, request));
    }

    private static void Apply(ItemsRepeater repeater, ScrollViewer scroller, MessageScrollRequest request)
    {
        scroller.UpdateLayout();
        if (request.Target == MessageScrollTarget.Bottom)
        {
            scroller.ChangeView(null, scroller.ScrollableHeight, null, disableAnimation: true);
            return;
        }

        var index = IndexOf(repeater, request.MessageId);
        if (index < 0)
        {
            return;
        }

        // ItemsRepeater виртуализирует, поэтому нужное сообщение может быть ещё не создано:
        // в WPF-версии контейнеры существовали всегда и достаточно было их найти.
        if (repeater.GetOrCreateElement(index) is not FrameworkElement element)
        {
            return;
        }

        repeater.UpdateLayout();
        var bounds = element
            .TransformToVisual(scroller)
            .TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        var offset = scroller.VerticalOffset + bounds.Top - ((scroller.ViewportHeight - bounds.Height) / 2);
        scroller.ChangeView(null, Math.Max(0, offset), null, disableAnimation: true);
    }

    private static int IndexOf(ItemsRepeater repeater, long? messageId)
    {
        if (messageId is not { } id || repeater.ItemsSourceView is not { } source)
        {
            return -1;
        }

        for (var index = 0; index < source.Count; index++)
        {
            if (source.GetAt(index) is MessageItemViewModel message && message.Id == id)
            {
                return index;
            }
        }

        return -1;
    }

    private static void OnElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs eventArgs)
    {
        if (eventArgs.Element is FrameworkElement element)
        {
            element.EffectiveViewportChanged += OnEffectiveViewportChanged;
        }
    }

    private static void OnElementClearing(ItemsRepeater sender, ItemsRepeaterElementClearingEventArgs eventArgs)
    {
        if (eventArgs.Element is FrameworkElement element)
        {
            element.EffectiveViewportChanged -= OnEffectiveViewportChanged;
        }
    }

    /// <summary>
    /// Событийная модель вместо обхода всех сообщений на каждый проход раскладки, как было в WPF:
    /// <c>EffectiveViewport</c> уже задан в координатах самого элемента и учитывает обрезку предками.
    /// </summary>
    private static void OnEffectiveViewportChanged(FrameworkElement element, EffectiveViewportChangedEventArgs eventArgs)
    {
        if (VisualTreeHelper.GetParent(element) is not ItemsRepeater repeater
            || GetVisibleMessageCommand(repeater) is not { } command
            || element.ActualHeight <= 0)
        {
            return;
        }

        var index = repeater.GetElementIndex(element);
        if (index < 0 || repeater.ItemsSourceView?.GetAt(index) is not MessageItemViewModel message)
        {
            return;
        }

        var visible = new Rect(0, 0, element.ActualWidth, element.ActualHeight);
        visible.Intersect(eventArgs.EffectiveViewport);
        if (!visible.IsEmpty && visible.Height / element.ActualHeight >= VisibleRatio && command.CanExecute(message))
        {
            command.Execute(message);
        }
    }
}
