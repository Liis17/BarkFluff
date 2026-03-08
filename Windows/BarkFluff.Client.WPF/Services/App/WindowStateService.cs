using BarkFluff.Client.WPF.Reactive;

using System.Windows;

namespace BarkFluff.Client.WPF.Services.App
{
    // Добавили интерфейс IDisposable, чтобы явно указывать, что класс требует очистки
    public class WindowStateService : IDisposable
    {
        private Window? _window;

        // Убрали отдельные поля _isActivated и _isNotMinimized, 
        // так как надежнее читать свойства напрямую из _window при событиях,
        // либо хранить их, но инициализировать правильно.
        // Ниже приведен вариант с полями, но с правильной инициализацией.
        private bool _isActivated;
        private bool _isNotMinimized;

        public ReactiveBool IsApplicationActive { get; } = new ReactiveBool(false);

        public void Initialize(Window window)
        {
            if (_window != null)
            {
                UnsubscribeFromWindowEvents(_window);
            }

            _window = window;

            // --- ИСПРАВЛЕНИЕ ТУТ ---
            // Считываем начальное состояние ПРЯМО СЕЙЧАС, а не ждем событий
            _isActivated = _window.IsActive;
            _isNotMinimized = _window.WindowState != WindowState.Minimized;
            // -----------------------

            SubscribeToWindowEvents(_window);
            UpdateApplicationActiveState();
        }

        private void SubscribeToWindowEvents(Window window)
        {
            window.Activated += OnWindowActivated;
            window.Deactivated += OnWindowDeactivated;
            window.StateChanged += OnWindowStateChanged;

            // Также полезно следить за закрытием, чтобы избежать утечек памяти
            window.Closed += OnWindowClosed;
        }

        private void UnsubscribeFromWindowEvents(Window window)
        {
            window.Activated -= OnWindowActivated;
            window.Deactivated -= OnWindowDeactivated;
            window.StateChanged -= OnWindowStateChanged;
            window.Closed -= OnWindowClosed;
        }

        private void OnWindowActivated(object? sender, EventArgs e)
        {
            _isActivated = true;

            // Важный момент: при активации (клике по иконке в панели задач)
            // окно может восстановиться из свернутого состояния, но событие StateChanged
            // может сработать чуть позже или в другом порядке.
            // Надежнее проверить состояние окна тут же:
            if (_window != null)
                _isNotMinimized = _window.WindowState != WindowState.Minimized;

            UpdateApplicationActiveState();
        }

        private void OnWindowDeactivated(object? sender, EventArgs e)
        {
            _isActivated = false;
            UpdateApplicationActiveState();
        }

        private void OnWindowStateChanged(object? sender, EventArgs e)
        {
            if (_window != null)
            {
                _isNotMinimized = _window.WindowState != WindowState.Minimized;
                // При сворачивании окна (Minimize) WPF обычно также вызывает Deactivated.
                // При разворачивании (Normal/Maximized) окно обычно активируется.
                // Но лучше просто обновить статус.
                UpdateApplicationActiveState();
            }
        }

        private void OnWindowClosed(object? sender, EventArgs e)
        {
            Dispose(); // Автоматическая очистка при закрытии окна
        }

        private void UpdateApplicationActiveState()
        {
            // Логика: активно только если в фокусе И не свернуто
            bool newState = _isActivated && _isNotMinimized;

            if (IsApplicationActive.Value != newState)
            {
                IsApplicationActive.Value = newState;
            }
        }

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