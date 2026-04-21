using BarkFluff.Users.Domain;
using BarkFluff.Users.Persistence.Contexts;

using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Users.Persistence.Services;

public class PersonalizationStorage
{
    private readonly UsersContext _usersContext;

    public PersonalizationStorage(UsersContext usersContext)
    {
        _usersContext = usersContext;
    }

    public Task<UserPersonalization?> Get(long userId)
    {
        return _usersContext.UserPersonalizations.FirstOrDefaultAsync(p => p.UserId == userId);
    }

    public async Task<UserPersonalization> GetOrCreate(long userId)
    {
        var existing = await Get(userId);
        if (existing is not null)
        {
            return existing;
        }

        return await Create(userId);
    }

    public async Task<UserPersonalization> Create(long userId)
    {
        var personalization = new UserPersonalization { UserId = userId };
        _usersContext.UserPersonalizations.Add(personalization);
        await _usersContext.SaveChangesAsync();
        return personalization;
    }

    public async Task<UserPersonalization> Update(long userId, string? profilePosterFileId, string[] chatBackgroundFileIds)
    {
        var personalization = await GetOrCreate(userId);

        personalization.ProfilePosterFileId = profilePosterFileId;
        personalization.ChatBackgroundFileIds = chatBackgroundFileIds;

        await _usersContext.SaveChangesAsync();
        return personalization;
    }
}
