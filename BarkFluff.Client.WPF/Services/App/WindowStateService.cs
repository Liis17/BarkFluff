using BarkFluff.Client.WPF.Reactive;

using System.Windows;

namespace BarkFluff.Client.WPF.Services.App
{
    /// <summary>
    /// Сервис для отслеживания состояния окна приложения.
    /// Определяет, активно ли приложение (в фокусе и развернуто).
    /// </summary>
    public class WindowStateService
    {
        private Window? _window;
        private bool _isActivated;
        private bool _isNotMinimized;

        /// <summary>
        /// Реактивное поле, которое показывает, активно ли приложение.
        /// True - приложение в фокусе и развернуто, False - в остальных случаях.
        /// </summary>
        public ReactiveBool IsApplicationActive { get; } = new ReactiveBool(false);

        /// <summary>
        /// Инициализирует сервис отслеживания состояния окна.
        /// </summary>
        /// <param name="window">Главное окно приложения для отслеживания.</param>
        public void Initialize(Window window)
        {
            if (_window != null)
            {
                UnsubscribeFromWindowEvents(_window);
            }

            _window = window;
            SubscribeToWindowEvents(_window);
            UpdateApplicationActiveState();
        }

        /// <summary>
        /// Подписывается на события окна.
        /// </summary>
        private void SubscribeToWindowEvents(Window window)
        {
            window.Activated += OnWindowActivated;
            window.Deactivated += OnWindowDeactivated;
            window.StateChanged += OnWindowStateChanged;
        }

        /// <summary>
        /// Отписывается от событий окна.
        /// </summary>
        private void UnsubscribeFromWindowEvents(Window window)
        {
            window.Activated -= OnWindowActivated;
            window.Deactivated -= OnWindowDeactivated;
            window.StateChanged -= OnWindowStateChanged;
        }

        /// <summary>
        /// Обработчик события активации окна (получение фокуса).
        /// </summary>
        private void OnWindowActivated(object? sender, EventArgs e)
        {
            _isActivated = true;
            UpdateApplicationActiveState();
        }

        /// <summary>
        /// Обработчик события деактивации окна (потеря фокуса).
        /// </summary>
        private void OnWindowDeactivated(object? sender, EventArgs e)
        {
            _isActivated = false;
            UpdateApplicationActiveState();
        }

        /// <summary>
        /// Обработчик события изменения состояния окна (свернуто/развернуто/нормальное).
        /// </summary>
        private void OnWindowStateChanged(object? sender, EventArgs e)
        {
            if (_window != null)
            {
                _isNotMinimized = _window.WindowState != WindowState.Minimized;
                UpdateApplicationActiveState();
            }
        }

        /// <summary>
        /// Обновляет состояние IsApplicationActive на основе текущего состояния окна.
        /// </summary>
        private void UpdateApplicationActiveState()
        {
            bool newState = _isActivated && _isNotMinimized;

            if (IsApplicationActive.Value != newState)
            {
                IsApplicationActive.Value = newState;
            }
        }

        /// <summary>
        /// Освобождает ресурсы и отписывается от событий.
        /// </summary>
        public void Dispose()
        {
            if (_window != null)
            {
                UnsubscribeFromWindowEvents(_window);
                _window = null;
            }
        }
    }
}
