namespace BarkFluff.Users.Features.ChatFolders;

/// <summary>
/// Команда, меняющая папки чатов текущего пользователя. После её успешного выполнения
/// <see cref="ChatFoldersChangedBehavior{TRequest,TResponse}"/> уведомляет другие устройства.
/// </summary>
public interface IChatFoldersMutation
{
}
