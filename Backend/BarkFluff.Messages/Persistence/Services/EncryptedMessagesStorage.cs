using BarkFluff.Messages.Domain;
using BarkFluff.Messages.Persistence.Exceptions;

using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Messages.Persistence.Services;

public class EncryptedMessagesStorage(MessagesContext context)
{
    public async Task<EncryptedMessage> AddAsync(
        Guid chatId,
        long senderId,
        Guid senderDeviceId,
        byte[] ciphertext,
        byte[] nonce,
        byte[] associatedData)
    {
        var message = new EncryptedMessage
        {
            ChatId = chatId,
            SenderId = senderId,
            SenderDeviceId = senderDeviceId,
            SentAt = DateTime.UtcNow,
            Ciphertext = ciphertext,
            Nonce = nonce,
            AssociatedData = associatedData,
        };

        var result = await context.EncryptedMessages.AddAsync(message);
        await context.SaveChangesAsync();

        return result.Entity;
    }

    public Task<EncryptedMessage?> GetByIdAsync(long id)
    {
        return context.EncryptedMessages
            .FirstOrDefaultAsync(m => m.Id == id);
    }

    /// <summary>
    /// Двунаправленная пагинация шифрованных сообщений вокруг опорного fromMessageId.
    /// Если fromMessageId == null — последние offsetBefore (или 50, если меньше) сообщений.
    /// IsDeleted сообщения отдаются: клиент должен показать их как «удалено» (без расшифровки).
    /// </summary>
    public async Task<List<EncryptedMessage>> ListByChatAsync(
        Guid chatId,
        long? fromMessageId,
        int offsetBefore,
        int offsetAfter)
    {
        const int MaxOffset = 50;

        if (fromMessageId == null)
        {
            var count = offsetBefore > 0 ? Math.Min(offsetBefore, MaxOffset) : MaxOffset;

            var latest = await context.EncryptedMessages
                .Where(x => x.ChatId == chatId)
                .OrderByDescending(m => m.SentAt)
                .Take(count)
                .ToListAsync();

            return latest.OrderBy(m => m.SentAt).ToList();
        }

        var reference = await context.EncryptedMessages
            .FirstOrDefaultAsync(m => m.Id == fromMessageId && m.ChatId == chatId);

        if (reference == null)
        {
            throw new FromMessageNotFoundException();
        }

        var result = new List<EncryptedMessage>();

        if (offsetBefore > 0)
        {
            var before = await context.EncryptedMessages
                .Where(x => x.ChatId == chatId && x.SentAt < reference.SentAt)
                .OrderByDescending(m => m.SentAt)
                .Take(Math.Min(offsetBefore, MaxOffset))
                .ToListAsync();

            result.AddRange(before);
        }

        result.Add(reference);

        if (offsetAfter > 0)
        {
            var after = await context.EncryptedMessages
                .Where(x => x.ChatId == chatId && x.SentAt > reference.SentAt)
                .OrderBy(m => m.SentAt)
                .Take(Math.Min(offsetAfter, MaxOffset))
                .ToListAsync();

            result.AddRange(after);
        }

        return result.OrderBy(m => m.SentAt).ToList();
    }

    public async Task<EncryptedMessage> EditAsync(
        long messageId,
        byte[] ciphertext,
        byte[] nonce,
        byte[] associatedData)
    {
        var message = await context.EncryptedMessages
            .FirstOrDefaultAsync(m => m.Id == messageId);

        if (message == null)
        {
            throw new InvalidOperationException("Шифрованное сообщение не найдено");
        }

        if (message.IsDeleted)
        {
            throw new InvalidOperationException("Удалённое сообщение нельзя редактировать");
        }

        message.Ciphertext = ciphertext;
        message.Nonce = nonce;
        message.AssociatedData = associatedData;
        message.IsEdited = true;
        message.EditedAt = DateTime.UtcNow;

        await context.SaveChangesAsync();
        return message;
    }

    /// <summary>
    /// Soft-delete: помечает IsDeleted=true и обнуляет ciphertext/nonce/associated_data,
    /// чтобы шифротекст физически удалился из БД.
    /// </summary>
    public async Task<bool> SoftDeleteAsync(long messageId)
    {
        var message = await context.EncryptedMessages
            .FirstOrDefaultAsync(m => m.Id == messageId);

        if (message == null || message.IsDeleted)
        {
            return false;
        }

        message.IsDeleted = true;
        message.Ciphertext = Array.Empty<byte>();
        message.Nonce = Array.Empty<byte>();
        message.AssociatedData = Array.Empty<byte>();

        await context.SaveChangesAsync();
        return true;
    }

    public async Task<long> MarkReadThroughAsync(Guid chatId, long userId, long requestedMessageId)
    {
        var lastExistingId = await context.EncryptedMessages
            .Where(m => m.ChatId == chatId && !m.IsDeleted && m.Id <= requestedMessageId)
            .MaxAsync(m => (long?)m.Id) ?? 0;

        if (lastExistingId == 0)
        {
            return 0;
        }

        var state = await context.PrivateChatReadStates
            .FirstOrDefaultAsync(s => s.ChatId == chatId && s.UserId == userId);

        if (state is null)
        {
            await context.PrivateChatReadStates.AddAsync(new PrivateChatReadState
            {
                ChatId = chatId,
                UserId = userId,
                LastReadMessageId = lastExistingId,
            });
        }
        else if (lastExistingId > state.LastReadMessageId)
        {
            state.LastReadMessageId = lastExistingId;
        }
        else
        {
            return state.LastReadMessageId;
        }

        await context.SaveChangesAsync();
        return lastExistingId;
    }
}
