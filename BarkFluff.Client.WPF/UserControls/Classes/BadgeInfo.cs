namespace BarkFluff.Client.WPF.UserControls.Classes
{
    /// <summary>
    /// Информация о бадже пользователя
    /// </summary>
    public class BadgeInfo
    {
        /// <summary>
        /// URL изображения баджа (32x32 с сервера)
        /// </summary>
        public string ImageUrl { get; set; }

        /// <summary>
        /// Название баджа
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Описание баджа
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Приоритет отображения (меньше = выше приоритет)
        /// </summary>
        public int Priority { get; set; }
    }
}
