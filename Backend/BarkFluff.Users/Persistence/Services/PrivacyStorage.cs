using BarkFluff.Users.Domain;
using BarkFluff.Users.Persistence.Contexts;

using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Users.Persistence.Services;

public class PrivacyStorage
{
    private readonly UsersContext _usersContext;

    public PrivacyStorage(UsersContext usersContext)
    {
        _usersContext = usersContext;
    }

    public Task<Privacy?> Get(long userId)
    {
        return _usersContext.Privacies.FirstOrDefaultAsync(p => p.UserId == userId);
    }

    // Батч-чтение для GetUsersByUuid (этап 2.5) — без похода в БД на каждого пользователя.
    // Отсутствие строки трактуется как значения по умолчанию (Privacy ещё не создана).
    public Task<List<Privacy>> GetByUserIds(List<long> userIds)
    {
        return _usersContext.Privacies.Where(p => userIds.Contains(p.UserId)).ToListAsync();
    }

    public async Task<Privacy> GetOrCreate(long userId)
    {
        var existing = await Get(userId);
        if (existing is not null)
        {
            return existing;
        }

        return await Create(userId);
    }

    public async Task<Privacy> Create(long userId)
    {
        var privacy = new Privacy { UserId = userId };
        _usersContext.Privacies.Add(privacy);
        await _usersContext.SaveChangesAsync();
        return privacy;
    }

    public async Task<Privacy> Update(long userId, Privacy updated)
    {
        var privacy = await GetOrCreate(userId);

        privacy.ProfileVisibleOnSite = updated.ProfileVisibleOnSite;
        privacy.AvatarVisibility = updated.AvatarVisibility;
        privacy.BioVisibility = updated.BioVisibility;
        privacy.EmailVisibility = updated.EmailVisibility;
        privacy.SearchVisible = updated.SearchVisible;
        privacy.OnlineVisibility = updated.OnlineVisibility;
        privacy.DenyFederatedDm = updated.DenyFederatedDm;

        await _usersContext.SaveChangesAsync();
        return privacy;
    }
}
