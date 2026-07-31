namespace BarkFluff.Client.Core.Models;

/// <summary>Поля собственного профиля, доступные для правки в настройках.</summary>
public sealed record AccountProfile(string FirstName, string LastName, string Username, string Bio, string AvatarUrl);
