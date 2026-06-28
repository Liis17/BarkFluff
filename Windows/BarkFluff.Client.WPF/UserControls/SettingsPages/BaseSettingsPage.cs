using BarkFluff.Client.WPF.Services.App;

using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace BarkFluff.Client.WPF.UserControls.SettingsPages
{
    /// <summary>
    /// Базовый класс для всех страниц настроек.
    /// Предоставляет общую функциональность для навигации и реактивный заголовок,
    /// привязанный к ключу локализации (<see cref="TitleKey"/>) и обновляющийся
    /// при смене языка через <see cref="LanguageManager.LanguageChanged"/>.
    /// </summary>
    public class BaseSettingsPage : UserControl, INotifyPropertyChanged
    {
        /// <summary>
        /// Ключ ресурса для заголовка страницы. Переопределяется в наследниках.
        /// </summary>
        public virtual string TitleKey => "L_Settings_Default";

        /// <summary>
        /// Локализованный заголовок страницы — резолвится через FindResource по <see cref="TitleKey"/>.
        /// При смене языка через LanguageManager поднимается PropertyChanged, заголовок перечитывается.
        /// Помечен <c>virtual</c> для обратной совместимости с уже существующими наследниками,
        /// которые переопределяют <see cref="Title"/> литералом — постепенно мигрируем их на
        /// <see cref="TitleKey"/>, после чего этот fallback можно будет упростить.
        /// </summary>
        public virtual string Title
        {
            get
            {
                var app = Application.Current;
                if (app == null) return TitleKey;
                return app.TryFindResource(TitleKey) as string ?? TitleKey;
            }
        }

        /// <summary>
        /// Вызывается при нажатии кнопки "Назад".
        /// </summary>
        public event EventHandler? BackRequested;

        public event PropertyChangedEventHandler? PropertyChanged;

        public BaseSettingsPage()
        {
            LanguageManager.Instance.LanguageChanged += OnLanguageChanged;
            Unloaded += (_, _) => LanguageManager.Instance.LanguageChanged -= OnLanguageChanged;
        }

        private void OnLanguageChanged(object? sender, EventArgs e)
        {
            OnPropertyChanged(nameof(Title));
        }

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name ?? string.Empty));
        }

        /// <summary>
        /// Инициирует запрос на возврат к предыдущему экрану.
        /// </summary>
        protected void GoBack()
        {
            BackRequested?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Вызывается при переключении на эту страницу.
        /// </summary>
        public virtual void OnNavigatedTo()
        {
        }
    }
}
