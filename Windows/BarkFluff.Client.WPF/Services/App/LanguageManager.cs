using System.Globalization;
using System.Windows;

namespace BarkFluff.Client.WPF.Services.App
{
    /// <summary>
    /// Сервис управления языком интерфейса. Хранит текущий эффективный язык
    /// и публикует событие <see cref="LanguageChanged"/>, на которое подписывается
    /// C# код-бихайнд для обновления строк, прочитанных через <see cref="FrameworkElement.FindResource"/>.
    /// </summary>
    public sealed class LanguageManager
    {
        public static LanguageManager Instance { get; } = new LanguageManager();

        /// <summary>
        /// Текущий эффективный язык: "ru" | "en".
        /// "system" резолвится при <see cref="Apply"/> в один из этих кодов.
        /// </summary>
        public string CurrentLanguage { get; private set; } = "en";

        /// <summary>
        /// Поднимается после смены ResourceDictionary и Thread.CurrentUICulture.
        /// </summary>
        public event EventHandler? LanguageChanged;

        private LanguageManager() { }

        /// <summary>
        /// Резолвит выбор "system" в реальный код языка устройства
        /// (ru → ru, иначе → en). Явные "ru"/"en" пропускаются без изменений.
        /// </summary>
        public static string ResolveEffective(string requested)
        {
            if (requested == "ru" || requested == "en")
                return requested;

            // "system" или любое другое значение
            var os = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            return os == "ru" ? "ru" : "en";
        }

        /// <summary>
        /// Применяет язык: меняет ResourceDictionary в App.Current.Resources,
        /// ставит Thread.CurrentUICulture и поднимает событие <see cref="LanguageChanged"/>.
        /// </summary>
        public void Apply(string requested)
        {
            var effective = ResolveEffective(requested);
            CurrentLanguage = effective;

            BarkFluff.Client.WPF.App.ApplyLanguage(effective);

            var culture = new CultureInfo(effective);
            Thread.CurrentThread.CurrentUICulture = culture;
            Thread.CurrentThread.CurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            CultureInfo.DefaultThreadCurrentCulture = culture;

            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
