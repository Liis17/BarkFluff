namespace BarkFluff.Client.WPF.UserControls
{
    /// <summary>
    /// Тип аватара для отображения в контроле CachedAvatar
    /// </summary>
    public enum AvatarType
    {
        /// <summary>
        /// Обычный аватар - загружается изображение по FileId/PreviewFileId/FileUrl
        /// </summary>
        Image = 0,

        /// <summary>
        /// Избранный чат - отображается иконка закладки
        /// </summary>
        SavedChat = 1,

        /// <summary>
        /// Пользователь без аватара - отображается иконка человека
        /// </summary>
        UserWithoutAvatar = 2
    }
}
