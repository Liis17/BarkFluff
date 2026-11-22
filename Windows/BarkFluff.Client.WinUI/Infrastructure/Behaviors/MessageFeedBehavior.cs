using BarkFluff.Client.Core.Models;
using BarkFluff.Client.Core.ViewModels;

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

using System.Runtime.CompilerServices;
using System.Windows.Input;

using Windows.Foundation;

using VirtualKey = Windows.System.VirtualKey;

namespace BarkFluff.Client.WinUI.Infrastructure.Behaviors;

/// <summary>
/// Прокрутка ленты и отчёт о её положении. Свойства вешаются на <see cref="ItemsRepeater"/>,
/// сам <see cref="ScrollViewer"/> передаётся ссылкой: он лежит рядом с именем в разметке,
/// и поиск по визуальному дереву тут был бы лишним источником отказов.
/// </summary>
public static class MessageFeedBehavior
{
    private sealed class FeedState(ItemsRepeater repeater)
    {
        internal bool Queued;
        internal bool Intermediate;
        internal bool DirectManipulation;
        internal uint? ScrollBarPointer;
        internal bool UserScrollInProgress;
        internal bool ProgrammaticScroll;
        // Колесо и клавиши могут сдвинуть ленту позже или вовсе не сдвинуть её у кромки.
        internal readonly DispatcherQueueTimer InputTimer = repeater.DispatcherQueue.CreateTimer();
        internal bool HasScrollInput =>
            DirectManipulation || ScrollBarPointer.HasValue || UserScrollInProgress || InputTimer.IsRunning;
        internal void OnInputExpired(DispatcherQueueTimer sender, object args) => QueueViewportCheck(repeater);
        internal MessageScrollRequest? PendingScroll;
        internal long ExtentCallback;
        internal bool FollowingTail = true;
        internal MessageItemViewModel? LastBeforeHistoryUpdate;
        internal bool OlderArmed = true;
        internal bool NewerArmed = true;
        internal (double Offset, double Extent, double Viewport) View;
    }

    private static readonly ConditionalWeakTable<ItemsRepeater, FeedState> States = new();
    private static readonly PointerEventHandler WheelHandler = OnPointerWheelChanged;
    private static readonly KeyEventHandler ScrollKeyHandler = OnScrollKeyDown;
    private static readonly PointerEventHandler ScrollBarPressedHandler = OnScrollBarPressed;
    private static readonly PointerEventHandler ScrollBarReleasedHandler = OnScrollBarReleased;

    public static readonly DependencyProperty HistoryUpdatingProperty = DependencyProperty.RegisterAttached(
        "HistoryUpdating", typeof(bool), typeof(MessageFeedBehavior), new PropertyMetadata(false, OnHistoryUpdatingChanged));

    public static readonly DependencyProperty OlderHistoryCommandProperty = DependencyProperty.RegisterAttached(
        "OlderHistoryCommand", typeof(ICommand), typeof(MessageFeedBehavior), new PropertyMetadata(null));

    public static readonly DependencyProperty NewerHistoryCommandProperty = DependencyProperty.RegisterAttached(
        "NewerHistoryCommand", typeof(ICommand), typeof(MessageFeedBehavior), new PropertyMetadata(null));

    public static bool GetHistoryUpdating(DependencyObject element) => (bool)element.GetValue(HistoryUpdatingProperty);
    public static void SetHistoryUpdating(DependencyObject element, bool value) => element.SetValue(HistoryUpdatingProperty, value);
    public static ICommand? GetOlderHistoryCommand(DependencyObject element) => (ICommand?)element.GetValue(OlderHistoryCommandProperty);
    public static void SetOlderHistoryCommand(DependencyObject element, ICommand? value) => element.SetValue(OlderHistoryCommandProperty, value);
    public static ICommand? GetNewerHistoryCommand(DependencyObject element) => (ICommand?)element.GetValue(NewerHistoryCommandProperty);
    public static void SetNewerHistoryCommand(DependencyObject element, ICommand? value) => element.SetValue(NewerHistoryCommandProperty, value);

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
            Detach(repeater, previous);
            repeater.Loaded -= OnLoaded;
            repeater.Unloaded -= OnUnloaded;
        }

        if (eventArgs.NewValue is ScrollViewer scroller)
        {
            repeater.Loaded += OnLoaded;
            repeater.Unloaded += OnUnloaded;
            if (repeater.IsLoaded)
            {
                Attach(repeater, scroller);
            }
        }
    }

    private static void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (sender is ItemsRepeater repeater && GetScroller(repeater) is { } scroller)
        {
            Attach(repeater, scroller);
        }
    }

    private static void OnUnloaded(object sender, RoutedEventArgs args)
    {
        if (sender is ItemsRepeater repeater && GetScroller(repeater) is { } scroller)
        {
            Detach(repeater, scroller);
        }
    }

    private static void Attach(ItemsRepeater repeater, ScrollViewer scroller)
    {
        if (States.TryGetValue(repeater, out _))
        {
            return;
        }

        var state = new FeedState(repeater) { View = ReadView(scroller) };
        state.InputTimer.Interval = TimeSpan.FromMilliseconds(250);
        state.InputTimer.IsRepeating = false;
        state.InputTimer.Tick += state.OnInputExpired;
        States.Add(repeater, state);
        scroller.SetValue(OwnerProperty, repeater);
        scroller.ViewChanged += OnViewChanged;
        scroller.DirectManipulationStarted += OnDirectManipulationStarted;
        scroller.DirectManipulationCompleted += OnDirectManipulationCompleted;
        scroller.AddHandler(UIElement.PointerWheelChangedEvent, WheelHandler, handledEventsToo: true);
        scroller.AddHandler(UIElement.PreviewKeyDownEvent, ScrollKeyHandler, handledEventsToo: true);
        scroller.AddHandler(UIElement.PointerPressedEvent, ScrollBarPressedHandler, handledEventsToo: true);
        scroller.AddHandler(UIElement.PointerReleasedEvent, ScrollBarReleasedHandler, handledEventsToo: true);
        scroller.AddHandler(UIElement.PointerCanceledEvent, ScrollBarReleasedHandler, handledEventsToo: true);
        scroller.AddHandler(UIElement.PointerCaptureLostEvent, ScrollBarReleasedHandler, handledEventsToo: true);
        scroller.SizeChanged += OnScrollerSizeChanged;
        repeater.EffectiveViewportChanged += OnEffectiveViewportChanged;
        state.ExtentCallback = scroller.RegisterPropertyChangedCallback(ScrollViewer.ExtentHeightProperty,
            (_, _) => QueueViewportCheck(repeater));
        if (GetScrollRequest(repeater) is { } request)
        {
            QueueScroll(repeater, scroller, state, request);
        }
        QueueViewportCheck(repeater);
    }

    private static void Detach(ItemsRepeater repeater, ScrollViewer scroller)
    {
        if (!States.TryGetValue(repeater, out var state))
        {
            return;
        }

        scroller.ViewChanged -= OnViewChanged;
        scroller.DirectManipulationStarted -= OnDirectManipulationStarted;
        scroller.DirectManipulationCompleted -= OnDirectManipulationCompleted;
        scroller.RemoveHandler(UIElement.PointerWheelChangedEvent, WheelHandler);
        scroller.RemoveHandler(UIElement.PreviewKeyDownEvent, ScrollKeyHandler);
        scroller.RemoveHandler(UIElement.PointerPressedEvent, ScrollBarPressedHandler);
        scroller.RemoveHandler(UIElement.PointerReleasedEvent, ScrollBarReleasedHandler);
        scroller.RemoveHandler(UIElement.PointerCanceledEvent, ScrollBarReleasedHandler);
        scroller.RemoveHandler(UIElement.PointerCaptureLostEvent, ScrollBarReleasedHandler);
        state.InputTimer.Stop();
        state.InputTimer.Tick -= state.OnInputExpired;
        scroller.SizeChanged -= OnScrollerSizeChanged;
        repeater.EffectiveViewportChanged -= OnEffectiveViewportChanged;
        scroller.UnregisterPropertyChangedCallback(ScrollViewer.ExtentHeightProperty, state.ExtentCallback);
        scroller.ClearValue(OwnerProperty);
        States.Remove(repeater);
    }

    private static void OnEffectiveViewportChanged(FrameworkElement element, EffectiveViewportChangedEventArgs args) =>
        QueueViewportCheck((ItemsRepeater)element);

    private static void OnScrollerSizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (sender is ScrollViewer scroller && scroller.GetValue(OwnerProperty) is ItemsRepeater repeater)
        {
            QueueViewportCheck(repeater);
        }
    }

    private static void OnHistoryUpdatingChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        if (element is not ItemsRepeater repeater || !States.TryGetValue(repeater, out var state))
        {
            return;
        }

        if (args.NewValue is true)
        {
            state.LastBeforeHistoryUpdate ??= LastMessage(repeater);
        }
        else
        {
            QueueViewportCheck(repeater);
        }
    }

    private static MessageItemViewModel? LastMessage(ItemsRepeater repeater) =>
        repeater.ItemsSourceView is { Count: > 0 } source
            ? source.GetAt(source.Count - 1) as MessageItemViewModel : null;

    private static void OnViewChanged(object? sender, ScrollViewerViewChangedEventArgs eventArgs)
    {
        if (sender is not ScrollViewer scroller || scroller.GetValue(OwnerProperty) is not ItemsRepeater repeater)
        {
            return;
        }

        if (!States.TryGetValue(repeater, out var state))
        {
            return;
        }

        state.Intermediate = eventArgs.IsIntermediate;
        var view = ReadView(scroller);
        var programmatic = state.ProgrammaticScroll || state.PendingScroll is not null;
        var userInput = state.HasScrollInput;
        var scrolled = !programmatic && MessageFeedViewport.HasScrolled(state.View, view, userInput);
        state.View = view;
        if (!eventArgs.IsIntermediate)
        {
            state.ProgrammaticScroll = false;
        }
        if (!programmatic)
        {
            state.UserScrollInProgress = eventArgs.IsIntermediate && (state.UserScrollInProgress || (userInput && scrolled));
            if (scrolled)
            {
                state.InputTimer.Stop();
            }
        }
        var historyUpdating = state.LastBeforeHistoryUpdate is not null || GetHistoryUpdating(repeater);
        if (scrolled && (userInput || !historyUpdating))
        {
            state.FollowingTail = MessageFeedViewport.FollowTail(state.FollowingTail, scrolled,
                scroller.ScrollableHeight - view.Offset);
            Execute(GetFeedPositionCommand(repeater), state.FollowingTail);
            if (!historyUpdating)
            {
                CheckHistoryEdges(repeater, state, scroller, olderScroll: true, newerScroll: true);
            }
        }
        QueueViewportCheck(repeater);
    }

    private static void OnPointerWheelChanged(object sender, PointerRoutedEventArgs args)
    {
        // У самой кромки колесо может не сдвинуть ленту и ViewChanged не придёт.
        if (sender is ScrollViewer scroller && scroller.GetValue(OwnerProperty) is ItemsRepeater repeater &&
            States.TryGetValue(repeater, out var state))
        {
            var pointer = args.GetCurrentPoint(scroller).Properties;
            var delta = pointer.MouseWheelDelta;
            if (delta != 0 && !pointer.IsHorizontalMouseWheel)
            {
                MarkScrollInput(state);
                if (state.PendingScroll is null && !GetHistoryUpdating(repeater))
                {
                    CheckHistoryEdges(repeater, state, scroller, olderScroll: delta > 0, newerScroll: delta < 0);
                }
            }
        }
    }

    private static void OnScrollKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key is (VirtualKey.Up or VirtualKey.Down or VirtualKey.PageUp or VirtualKey.PageDown
            or VirtualKey.Home or VirtualKey.End or VirtualKey.Space) && GetState(sender) is { } state)
        {
            MarkScrollInput(state);
        }
    }

    private static void MarkScrollInput(FeedState state)
    {
        state.ProgrammaticScroll = false;
        state.InputTimer.Stop();
        state.InputTimer.Start();
    }

    private static void OnDirectManipulationStarted(object? sender, object args)
    {
        if (GetState(sender) is { } state)
        {
            state.ProgrammaticScroll = false;
            state.DirectManipulation = true;
        }
    }

    private static void OnDirectManipulationCompleted(object? sender, object args)
    {
        if (sender is ScrollViewer scroller && scroller.GetValue(OwnerProperty) is ItemsRepeater repeater &&
            States.TryGetValue(repeater, out var state))
        {
            state.DirectManipulation = false;
            QueueViewportCheck(repeater);
        }
    }

    private static void OnScrollBarPressed(object sender, PointerRoutedEventArgs args)
    {
        if (sender is not ScrollViewer scroller || GetState(sender) is not { } state ||
            !args.GetCurrentPoint(scroller).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var thumbPressed = false;
        for (var element = args.OriginalSource as UIElement; element is not null && element != scroller;
            element = VisualTreeHelper.GetParent(element) as UIElement)
        {
            if (element is ScrollBar)
            {
                if (thumbPressed)
                {
                    state.ScrollBarPointer = args.Pointer.PointerId;
                    state.ProgrammaticScroll = false;
                }
                else
                {
                    MarkScrollInput(state);
                }
                return;
            }
            thumbPressed |= element is Thumb;
        }
    }

    private static void OnScrollBarReleased(object sender, PointerRoutedEventArgs args)
    {
        if (sender is ScrollViewer scroller && scroller.GetValue(OwnerProperty) is ItemsRepeater repeater &&
            States.TryGetValue(repeater, out var state) && state.ScrollBarPointer == args.Pointer.PointerId)
        {
            state.ScrollBarPointer = null;
            QueueViewportCheck(repeater);
        }
    }

    private static FeedState? GetState(object? sender) =>
        sender is ScrollViewer scroller && scroller.GetValue(OwnerProperty) is ItemsRepeater repeater &&
        States.TryGetValue(repeater, out var state) ? state : null;

    private static (double Offset, double Extent, double Viewport) ReadView(ScrollViewer scroller) =>
        (scroller.VerticalOffset, scroller.ExtentHeight, scroller.ViewportHeight);

    private static void CheckHistoryEdges(ItemsRepeater repeater, FeedState state, ScrollViewer scroller,
        bool olderScroll = false, bool newerScroll = false)
    {
        var (offset, extent, viewport) = ReadView(scroller);
        var older = MessageFeedViewport.CheckHistoryEdge(state.OlderArmed, olderScroll, offset, extent, viewport);
        var newer = MessageFeedViewport.CheckHistoryEdge(state.NewerArmed, newerScroll,
            scroller.ScrollableHeight - offset, extent, viewport);
        state.OlderArmed = older.Armed;
        state.NewerArmed = newer.Armed;
        if (older.Load)
        {
            Execute(GetOlderHistoryCommand(repeater));
        }
        if (newer.Load)
        {
            Execute(GetNewerHistoryCommand(repeater));
        }
    }

    private static void QueueViewportCheck(ItemsRepeater repeater)
    {
        if (!States.TryGetValue(repeater, out var state) || state.Queued ||
            state.PendingScroll is not null || GetHistoryUpdating(repeater))
        {
            return;
        }

        state.Queued = repeater.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            state.Queued = false;
            if (!States.TryGetValue(repeater, out var current) || !ReferenceEquals(current, state) || state.PendingScroll is not null ||
                GetHistoryUpdating(repeater) || GetScroller(repeater) is not { ViewportHeight: > 0 } scroller)
            {
                return;
            }

            if (!state.HasScrollInput && !state.Intermediate)
            {
                state.View = ReadView(scroller);
            }
            if (state.LastBeforeHistoryUpdate is { } previousLast)
            {
                state.LastBeforeHistoryUpdate = null;
                // Страница снизу сохраняет позицию чтения, даже если до вставки она была у кромки.
                if (!ReferenceEquals(previousLast, LastMessage(repeater)) &&
                    !MessageFeedViewport.IsNearEdge(scroller.ScrollableHeight - scroller.VerticalOffset))
                {
                    state.FollowingTail = false;
                    Execute(GetFeedPositionCommand(repeater), false);
                }
            }
            if (state.FollowingTail && !state.HasScrollInput && !state.Intermediate && !state.ProgrammaticScroll &&
                scroller.VerticalOffset < scroller.ScrollableHeight - 0.5)
            {
                // Раскладка может увеличить высоту последней строки без прокрутки пользователя.
                state.View = (scroller.ScrollableHeight, scroller.ExtentHeight, scroller.ViewportHeight);
                if (ChangeView(scroller, state, scroller.ScrollableHeight))
                {
                    QueueViewportCheck(repeater);
                    return;
                }
            }
            if (!state.Intermediate)
            {
                ReportVisibleMessages(repeater, scroller);
            }
            CheckHistoryEdges(repeater, state, scroller);
        });
    }

    private static void Execute(ICommand? command, object? argument = null)
    {
        if (command?.CanExecute(argument) == true)
        {
            command.Execute(argument);
        }
    }

    private static void OnScrollRequestChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs eventArgs)
    {
        if (dependencyObject is not ItemsRepeater repeater ||
            GetScroller(repeater) is not { } scroller ||
            !States.TryGetValue(repeater, out var state))
        {
            return;
        }

        if (eventArgs.NewValue is MessageScrollRequest request)
        {
            QueueScroll(repeater, scroller, state, request);
        }
        else
        {
            state.OlderArmed = true;
            state.NewerArmed = true;
            state.PendingScroll = null;
            QueueViewportCheck(repeater);
        }
    }

    private static void QueueScroll(ItemsRepeater repeater, ScrollViewer scroller, FeedState state, MessageScrollRequest request)
    {
        state.PendingScroll = request;
        if (request.Target == MessageScrollTarget.Message)
        {
            state.OlderArmed = true;
            state.NewerArmed = true;
        }
        // Коллекция изменилась только что — раскладка ещё не прошла, измерять пока нечего.
        repeater.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (!States.TryGetValue(repeater, out var current) || !ReferenceEquals(current, state) ||
                !ReferenceEquals(state.PendingScroll, request))
            {
                return;
            }

            state.LastBeforeHistoryUpdate = null;
            var atBottom = Apply(repeater, scroller, state, request);
            if (atBottom.HasValue)
            {
                state.FollowingTail = atBottom.Value;
                Execute(GetFeedPositionCommand(repeater), state.FollowingTail);
            }
            state.View = ReadView(scroller);
            if (ReferenceEquals(state.PendingScroll, request))
            {
                state.PendingScroll = null;
                QueueViewportCheck(repeater);
            }
        });
    }

    private static bool ChangeView(ScrollViewer scroller, FeedState state, double offset)
    {
        state.ProgrammaticScroll = true;
        if (scroller.ChangeView(null, offset, null, disableAnimation: true))
        {
            return true;
        }

        state.ProgrammaticScroll = false;
        return false;
    }

    private static bool? Apply(ItemsRepeater repeater, ScrollViewer scroller, FeedState state, MessageScrollRequest request)
    {
        scroller.UpdateLayout();
        if (request.Target == MessageScrollTarget.Bottom)
        {
            ChangeView(scroller, state, scroller.ScrollableHeight);
            return true;
        }

        var index = IndexOf(repeater, request.MessageId);
        if (index < 0)
        {
            return null;
        }

        // ItemsRepeater виртуализирует, поэтому нужное сообщение может быть ещё не создано:
        // в WPF-версии контейнеры существовали всегда и достаточно было их найти.
        if (repeater.GetOrCreateElement(index) is not FrameworkElement element)
        {
            return null;
        }

        repeater.UpdateLayout();
        var bounds = element
            .TransformToVisual(scroller)
            .TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        var offset = MessageFeedViewport.CenterOffset(scroller.VerticalOffset, bounds.Top, bounds.Height,
            scroller.ViewportHeight, scroller.ScrollableHeight);
        ChangeView(scroller, state, offset);
        return MessageFeedViewport.IsNearEdge(scroller.ScrollableHeight - offset);
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

    private static void ReportVisibleMessages(ItemsRepeater repeater, ScrollViewer scroller)
    {
        var command = GetVisibleMessageCommand(repeater);
        var viewport = new Rect(0, 0, scroller.ViewportWidth, scroller.ViewportHeight);
        var count = VisualTreeHelper.GetChildrenCount(repeater);
        for (var child = 0; child < count; child++)
        {
            if (VisualTreeHelper.GetChild(repeater, child) is not FrameworkElement { ActualHeight: > 0 } element)
            {
                continue;
            }

            var index = repeater.GetElementIndex(element);
            if (index < 0 || repeater.ItemsSourceView?.GetAt(index) is not MessageItemViewModel message)
            {
                continue;
            }

            var bounds = element.TransformToVisual(scroller)
                .TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
            bounds.Intersect(viewport);
            if (!bounds.IsEmpty && bounds.Height / element.ActualHeight >= VisibleRatio)
            {
                Execute(command, message);
            }
        }
    }
}
