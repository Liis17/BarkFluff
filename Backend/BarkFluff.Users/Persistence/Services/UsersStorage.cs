using BarkFluff.Shared.Exceptions.Identity;
using BarkFluff.Users.Domain;
using BarkFluff.Users.Persistence.Contexts;

using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace BarkFluff.Users.Persistence.Services;

public class UsersStorage
{
    private readonly UsersContext _usersContext;

    public UsersStorage(UsersContext usersContext)
    {
        _usersContext = usersContext;
    }

    public async Task<User?> GetUserByUsername(string username)
    {
        var user = await _usersContext.Users.Include(u => u.Contact)
            .FirstOrDefaultAsync(x => string.Equals(x.Username.ToLower(), username.ToLower()));

        return user;
    }

    public async Task<User?> GetUserByEmail(string email)
    {
        var userContact = await _usersContext.UserContacts.Include(u => u.User)
            .FirstOrDefaultAsync(x => string.Equals(x.Email.ToLower(), email.ToLower()));

        return userContact?.User;
    }

    public async Task<User?> GetById(long id)
    {
        var user = await _usersContext.Users.Include(u => u.Contact).FirstOrDefaultAsync(x => x.Id == id);

        return user;
    }

    public async Task<List<User>> GetByIds(List<long> ids)
    {
        var users = await _usersContext.Users
            .Include(u => u.Contact)
            .Where(x => ids.Contains(x.Id))
            .ToListAsync();

        return users;
    }

    /// <summary>
    /// Метод для поиска пользователей по имени, фамилии или имени пользователя.
    /// Использует полнотекстовый поиск PostgreSQL для эффективного поиска с учетом нечеткого соответствия.
    /// </summary>
    /// <param name="searchTerm">Поисковый запрос</param>
    /// <param name="page">Номер страницы (начинается с 1)</param>
    /// <param name="pageSize">Размер страницы</param>
    /// <returns>Список пользователей и общее количество найденных пользователей</returns>
    public async Task<(List<User> Users, int TotalCount)> SearchUsers(string searchTerm, int page = 1, int pageSize = 10)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
        {
            return (new List<User>(), 0);
        }

        // Используем сырой SQL запрос для полнотекстового поиска
        // Это самый эффективный способ для полнотекстового поиска в PostgreSQL
        // Не требует настройки специальных функций в EF Core
        var skip = (page - 1) * pageSize;
        var normalizedSearchTerm = searchTerm.Trim().ToLower();

        var sql = @"
            SELECT u.""Id"", u.""FirstName"", u.""LastName"", u.""Username"", u.""RegistrationDate"", 
                   u.""ProfilePicture"", u.""ProfilePicturePreviewUrl"", u.""Bio"", u.""IsDraft"", u.""StorageLimitGb"",
                   uc.""Email"", uc.""UserId""
            FROM ""Users"" u
            LEFT JOIN ""UserContacts"" uc ON u.""Id"" = uc.""UserId""
            WHERE to_tsvector('russian', u.""FirstName"" || ' ' || u.""LastName"" || ' ' || u.""Username"") @@ plainto_tsquery('russian', @searchTerm)
            AND u.""IsDraft"" = false
            ORDER BY ts_rank(to_tsvector('russian', u.""FirstName"" || ' ' || u.""LastName"" || ' ' || u.""Username""), plainto_tsquery('russian', @searchTerm)) DESC
            LIMIT @pageSize OFFSET @skip";

        var users = await _usersContext.Users
            .FromSqlRaw(sql,
                new NpgsqlParameter("@searchTerm", normalizedSearchTerm),
                new NpgsqlParameter("@pageSize", pageSize),
                new NpgsqlParameter("@skip", skip))
            .Include(u => u.Contact)
            .ToListAsync();

        // Получение общего количества результатов для пагинации
        var countSql = @"
            SELECT COUNT(*)
            FROM ""Users"" u
            WHERE to_tsvector('russian', u.""FirstName"" || ' ' || u.""LastName"" || ' ' || u.""Username"") @@ plainto_tsquery('russian', @searchTerm)
            AND u.""IsDraft"" = false";

        var totalCount = await _usersContext.Database.ExecuteSqlRawAsync(countSql,
            new NpgsqlParameter("@searchTerm", normalizedSearchTerm));

        return (users, totalCount);
    }

    /// <summary>
    /// Альтернативный метод поиска пользователей, использующий триграммы PostgreSQL
    /// для нахождения похожих строк (например, с опечатками).
    /// </summary>
    /// <param name="searchTerm">Поисковый запрос</param>
    /// <param name="page">Номер страницы (начинается с 1)</param>
    /// <param name="pageSize">Размер страницы</param>
    /// <param name="similarityThreshold">Порог схожести (от 0 до 1, рекомендуется 0.3)</param>
    /// <returns>Список пользователей и общее количество найденных пользователей</returns>
    public async Task<(List<User> Users, int TotalCount)> SearchUsersByTrigram(string searchTerm, int skip = 0, int pageSize = 10, double similarityThreshold = 0.3, long? currentUserId = null, bool respectSearchVisibility = true)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
        {
            return (new List<User>(), 0);
        }

        var normalizedSearchTerm = searchTerm.Trim();

        // Фильтр приватности: пользователь, скрывший себя из поиска (Privacies.SearchVisible=false),
        // должен исключаться из выдачи — но всегда видеть сам себя.
        // Если строки Privacies ещё нет — считаем пользователя видимым (LEFT JOIN + IS NULL).
        var privacyFilter = respectSearchVisibility
            ? @" AND (p.""SearchVisible"" IS NULL OR p.""SearchVisible"" = true OR u.""Id"" = @currentUserId)"
            : string.Empty;

        // SQL запрос для поиска с использованием триграмм
        var sql = @"
            SELECT u.""Id"", u.""FirstName"", u.""LastName"", u.""Username"", u.""RegistrationDate"",
                   u.""ProfilePicture"", u.""ProfilePicturePreviewUrl"", u.""Bio"", u.""IsDraft"", u.""StorageLimitGb"",
                   uc.""Email"", uc.""UserId""
            FROM ""Users"" u
            LEFT JOIN ""UserContacts"" uc ON u.""Id"" = uc.""UserId""
            LEFT JOIN ""Privacies"" p ON u.""Id"" = p.""UserId""
            WHERE (similarity(u.""FirstName"", @searchTerm) > @threshold
               OR similarity(u.""LastName"", @searchTerm) > @threshold
               OR similarity(u.""Username"", @searchTerm) > @threshold)
            AND u.""IsDraft"" = false" + privacyFilter + @"
            ORDER BY GREATEST(
                similarity(u.""FirstName"", @searchTerm),
                similarity(u.""LastName"", @searchTerm),
                similarity(u.""Username"", @searchTerm)
            ) DESC
            LIMIT @pageSize OFFSET @skip";

        var users = await _usersContext.Users
            .FromSqlRaw(sql,
                new NpgsqlParameter("@searchTerm", normalizedSearchTerm),
                new NpgsqlParameter("@threshold", similarityThreshold),
                new NpgsqlParameter("@pageSize", pageSize),
                new NpgsqlParameter("@skip", skip),
                new NpgsqlParameter("@currentUserId", (object?)currentUserId ?? DBNull.Value))
            .Include(u => u.Contact)
            .ToListAsync();

        // Запрос для получения общего количества
        var countSql = @"
            SELECT COUNT(*)
            FROM ""Users"" u
            LEFT JOIN ""Privacies"" p ON u.""Id"" = p.""UserId""
            WHERE (similarity(u.""FirstName"", @searchTerm) > @threshold
               OR similarity(u.""LastName"", @searchTerm) > @threshold
               OR similarity(u.""Username"", @searchTerm) > @threshold)
            AND u.""IsDraft"" = false" + privacyFilter;

        var totalCount = await _usersContext.Database.ExecuteSqlRawAsync(countSql,
            new NpgsqlParameter("@searchTerm", normalizedSearchTerm),
            new NpgsqlParameter("@threshold", similarityThreshold),
            new NpgsqlParameter("@currentUserId", (object?)currentUserId ?? DBNull.Value));

        return (users, totalCount);
    }

    public async Task<User> CreateUser(string username, string firstName, string lastName, string email)
    {
        var contactUser = new UserContact { Email = email };

        var unixTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var user = new User
        {
            Id = unixTimestamp,
            Username = username,
            FirstName = firstName,
            LastName = lastName,
            RegistrationDate = DateTime.UtcNow,
            Contact = contactUser,
            IsDraft = true,
        };

        await _usersContext.Users.AddAsync(user);

        await _usersContext.SaveChangesAsync();

        return user;
    }

    public async Task ChangeDraftStatus(long userId, bool isDraft)
    {
        var user = await _usersContext.Users.FirstOrDefaultAsync(x => x.Id == userId);

        if (user is null)
        {
            throw new UserNotFoundException();
        }

        user.IsDraft = isDraft;

        await _usersContext.SaveChangesAsync();
    }

    public async Task UpdateProfilePicture(long userId, string profilePictureUrl, string profilePicturePreviewUrl)
    {
        var user = await _usersContext.Users.FirstOrDefaultAsync(x => x.Id == userId);

        if (user is null)
        {
            throw new UserNotFoundException();
        }

        user.ProfilePicture = profilePictureUrl;
        user.ProfilePicturePreviewUrl = profilePicturePreviewUrl;
        await _usersContext.SaveChangesAsync();
    }


    public async Task UpdateTrackedUser(User user)
    {
        _usersContext.Users.Update(user);

        await _usersContext.SaveChangesAsync();
    }

    public async Task ChangeName(long userId, string firstName, string lastName)
    {
        var user = await _usersContext.Users.FirstOrDefaultAsync(x => x.Id == userId);

        if (user is null)
        {
            throw new UserNotFoundException();
        }

        user.FirstName = firstName;
        user.LastName = lastName;

        await _usersContext.SaveChangesAsync();
    }

    public async Task ChangeUsername(long userId, string username)
    {
        var user = await _usersContext.Users.FirstOrDefaultAsync(x => x.Id == userId);
        if (user is null)
        {
            throw new UserNotFoundException();
        }

        user.Username = username;

        await _usersContext.SaveChangesAsync();
    }

    public async Task ChangeBio(long userId, string newBio)
    {
        var user = await _usersContext.Users.FirstOrDefaultAsync(x => x.Id == userId);

        if (user is null)
        {
            throw new UserNotFoundException();
        }

        user.Bio = newBio;

        await _usersContext.SaveChangesAsync();
    }

    // Методы для работы с баджами

    public async Task<List<UserBadge>> GetUserBadgesAsync(long userId, int? limit = null)
    {
        var query = _usersContext.UserBadges
            .Include(ub => ub.Badge)
            .Where(ub => ub.UserId == userId && ub.Badge.IsActive)
            .OrderBy(ub => ub.Priority)
            .ThenBy(ub => ub.AssignedDate);

        if (limit.HasValue)
        {
            query = (IOrderedQueryable<UserBadge>)query.Take(limit.Value);
        }

        return await query.ToListAsync();
    }

    public async Task<UserBadge> AssignBadgeToUserAsync(long userId, int badgeId, int priority)
    {
        var userBadge = new UserBadge
        {
            UserId = userId,
            BadgeId = badgeId,
            Priority = priority,
            AssignedDate = DateTime.UtcNow
        };

        await _usersContext.UserBadges.AddAsync(userBadge);
        await _usersContext.SaveChangesAsync();

        // Загружаем связанный бадж для возврата
        await _usersContext.Entry(userBadge).Reference(ub => ub.Badge).LoadAsync();

        return userBadge;
    }

    public async Task<bool> RemoveBadgeFromUserAsync(long userId, int badgeId)
    {
        var userBadge = await _usersContext.UserBadges
            .FirstOrDefaultAsync(ub => ub.UserId == userId && ub.BadgeId == badgeId);

        if (userBadge == null)
        {
            return false;
        }

        _usersContext.UserBadges.Remove(userBadge);
        await _usersContext.SaveChangesAsync();

        return true;
    }

    public async Task<UserBadge?> UpdateUserBadgePriorityAsync(long userId, int badgeId, int newPriority)
    {
        var userBadge = await _usersContext.UserBadges
            .Include(ub => ub.Badge)
            .FirstOrDefaultAsync(ub => ub.UserId == userId && ub.BadgeId == badgeId);

        if (userBadge == null)
        {
            return null;
        }

        userBadge.Priority = newPriority;
        await _usersContext.SaveChangesAsync();

        return userBadge;
    }

    public async Task<Badge> CreateBadgeAsync(Badge badge)
    {
        badge.CreatedDate = DateTime.UtcNow;
        badge.IsActive = true;

        await _usersContext.Badges.AddAsync(badge);
        await _usersContext.SaveChangesAsync();

        return badge;
    }

    public async Task<List<Badge>> GetAllBadgesAsync(bool includeInactive = false)
    {
        var query = _usersContext.Badges.AsQueryable();

        if (!includeInactive)
        {
            query = query.Where(b => b.IsActive);
        }

        return await query.OrderBy(b => b.Name).ToListAsync();
    }

    public async Task<Badge?> UpdateBadgeAsync(int id, string name, string description, string imageUrl, bool isActive)
    {
        var badge = await _usersContext.Badges.FindAsync(id);

        if (badge is null)
            return null;

        badge.Name = name;
        badge.Description = description;
        badge.ImageUrl = imageUrl;
        badge.IsActive = isActive;

        await _usersContext.SaveChangesAsync();

        return badge;
    }

    public async Task<bool> DeleteBadgeAsync(int id)
    {
        var badge = await _usersContext.Badges.FindAsync(id);

        if (badge is null)
            return false;

        _usersContext.Badges.Remove(badge);
        await _usersContext.SaveChangesAsync();

        return true;
    }

    public async Task<(List<User> Users, int TotalCount)> GetAllUsersDescending(int offset, int size)
    {
        var query = _usersContext.Users
            .Include(u => u.Contact)
            .Where(u => !u.IsDraft)
            .OrderByDescending(u => u.Id);

        var totalCount = await query.CountAsync();
        var users = await query.Skip(offset).Take(size).ToListAsync();

        return (users, totalCount);
    }

    public async Task UpdateStorageLimitGb(long userId, int limitGb)
    {
        var user = await _usersContext.Users.FirstOrDefaultAsync(x => x.Id == userId);

        if (user is null)
            throw new UserNotFoundException();

        user.StorageLimitGb = limitGb;

        await _usersContext.SaveChangesAsync();
    }
}