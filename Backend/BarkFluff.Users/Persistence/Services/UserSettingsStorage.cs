using BarkFluff.Users.Domain;
using BarkFluff.Users.Persistence.Contexts;

using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Users.Persistence.Services;

public class UserSettingsStorage(UsersContext context)
{
    public Task<UserSettings?> Get(long userId)
    {
        return context.UserSettings.FirstOrDefaultAsync(s => s.UserId == userId);
    }

    public async Task<UserSettings> GetOrCreate(long userId)
    {
        var settings = await Get(userId);
        if (settings is not null)
        {
            return settings;
        }

        settings = new UserSettings { UserId = userId };
        context.UserSettings.Add(settings);
        await context.SaveChangesAsync();
        return settings;
    }

    public async Task<UserSettings> SetGlobalChatBackground(long userId, string? fileId)
    {
        var settings = await GetOrCreate(userId);
        settings.GlobalChatBackgroundFileId = fileId;
        await context.SaveChangesAsync();
        return settings;
    }

    public Task<List<UserChatSettings>> GetChatSettings(long userId)
    {
        return context.UserChatSettings
            .Where(s => s.UserId == userId)
            .OrderBy(s => s.ChatId)
            .ToListAsync();
    }

    public async Task SetChatBackground(long userId, Guid chatId, string? fileId)
    {
        var setting = await context.UserChatSettings
            .FirstOrDefaultAsync(s => s.UserId == userId && s.ChatId == chatId);

        if (string.IsNullOrEmpty(fileId))
        {
            if (setting is not null)
            {
                context.UserChatSettings.Remove(setting);
                await context.SaveChangesAsync();
            }

            return;
        }

        if (setting is null)
        {
            context.UserChatSettings.Add(new UserChatSettings
            {
                UserId = userId,
                ChatId = chatId,
                ChatBackgroundFileId = fileId,
            });
        }
        else
        {
            setting.ChatBackgroundFileId = fileId;
        }

        await context.SaveChangesAsync();
    }

    public async Task ClearBackgroundReferences(long userId, IReadOnlyCollection<string> deletedFileIds)
    {
        if (deletedFileIds.Count == 0)
        {
            return;
        }

        var settings = await Get(userId);
        if (settings is not null && settings.GlobalChatBackgroundFileId is not null
            && deletedFileIds.Contains(settings.GlobalChatBackgroundFileId))
        {
            settings.GlobalChatBackgroundFileId = null;
        }

        var chatSettings = await context.UserChatSettings
            .Where(s => s.UserId == userId && deletedFileIds.Contains(s.ChatBackgroundFileId))
            .ToListAsync();
        context.UserChatSettings.RemoveRange(chatSettings);

        if (settings is not null || chatSettings.Count > 0)
        {
            await context.SaveChangesAsync();
        }
    }
}
