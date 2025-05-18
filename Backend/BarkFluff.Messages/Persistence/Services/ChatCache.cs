using Microsoft.Extensions.Caching.Distributed;

namespace BarkFluff.Messages.Persistence.Services;

public class ChatCache
{
    private readonly IDistributedCache _cache;

    public ChatCache(IDistributedCache cache)
    {
        _cache = cache;
    }

    public async Task<string?> GetChatName(Guid chatId, long userId)
    {
        try
        {
            var chatName = await _cache.GetStringAsync($"chat_name_{chatId}_{userId}");

            return chatName;
        }
        catch (Exception e)
        {
            return null;
        }
    }

    public async Task SetChatName(Guid chatId, long userId, string name)
    {
        try
        {
            await _cache.SetStringAsync($"chat_name_{chatId}_{userId}", name);
        }
        catch (Exception e)
        {
            //
        }
    }

    public async Task<string?> GetChatImage(Guid chatId, long userId)
    {
        try
        {
            var chatImage = await _cache.GetStringAsync($"chat_image_{chatId}_{userId}");

            return chatImage;
        }
        catch
        {
            return null;
        }
    }
    
    public async Task SetChatImage(Guid chatId, long userId, string image)
    {
        try
        {
            await _cache.SetStringAsync($"chat_image_{chatId}_{userId}", image);
        }
        catch (Exception e)
        {
            //
        }
    }
}