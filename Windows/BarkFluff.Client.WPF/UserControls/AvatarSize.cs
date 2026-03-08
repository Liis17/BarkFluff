namespace BarkFluff.Client.WPF.UserControls
{
    /// <summary>
    /// Тип аватара для отображения в контроле CachedAvatar
    /// </summary>
    public enum AvatarSize
    {
        /// <summary>
        /// Аватар для отображения в списках чатов и контактов 50x50
        /// </summary>
        Normal = 0,

        /// <summary>
        /// Аватар для отображения в заголовке чата и в сообщениях 35x35
        /// </summary>
        Little = 1,

        /// <summary>
        /// Аватар для отображения в профиле пользователя 110x110
        /// </summary>
        Big = 2,

        /// <summary>
        /// Аватар для отображения на рамке окна 23x23
        /// </summary>
        VeryLittle = 3
    }
}
