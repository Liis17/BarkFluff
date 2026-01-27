using System.Windows;
using System.Windows.Controls;

namespace BarkFluff.Client.WPF.UserControls.SettingsPages
{
    /// <summary>
    /// Базовый класс для всех страниц настроек.
    /// Предоставляет общую функциональность для навигации.
    /// </summary>
    public class BaseSettingsPage : UserControl
    {
        /// <summary>
        /// Заголовок страницы настроек.
        /// </summary>
        public virtual string Title => "Настройки";

        /// <summary>
        /// Вызывается при нажатии кнопки "Назад".
        /// </summary>
        public event EventHandler? BackRequested;

        /// <summary>
        /// Инициирует запрос на возврат к предыдущему экрану.
        /// </summary>
        protected void GoBack()
        {
            BackRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}
