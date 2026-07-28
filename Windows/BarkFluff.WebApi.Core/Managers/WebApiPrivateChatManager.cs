using BarkFluff.WebApi.Core.Crypto;
using BarkFluff.WebApi.Core.MessengerData;
using BarkFluff.WebApi.Core.MessengerData.NonSavedData;

using Google.Protobuf;

using System.Security.Cryptography;
using System.Text;

namespace BarkFluff.WebApi.Core.Managers
{
    /// <summary>
    /// Приватные чаты: E2E через общую кодовую фразу. Сервер хранит только шифротекст
    /// и метаданные (salt + verifier), расшифровать не может.
    /// </summary>
    /// <remarks>
    /// Ключ чата библиотека не хранит: методы принимают его параметром, а время жизни
    /// (память сессии, защищённое хранилище, повторный ввод фразы) определяет приложение.
    /// Получить ключ можно из <see cref="CreatePrivateChat"/>, <see cref="AcceptPrivateChat"/>
    /// или <see cref="UnlockChat"/>.
    /// </remarks>
    internal class WebApiPrivateChatManager : WebApiBase
    {
        private const int DefaultPageSize = 50;

        private readonly WebApi _webApi;

        public WebApiPrivateChatManager(WebApi webApi) : base(webApi)
        {
            _webApi = webApi;
        }

        /// <summary>
        /// Создать приватный чат с пользователем. Salt и verifier считаются локально,
        /// на сервер уходит только они — сама кодовая фраза не покидает клиент.
        /// Возвращает ключ чата, его нужно сохранить, чтобы читать и писать сообщения.
        /// </summary>
        public async Task<(ErrorReturner error, Proto.Messages.Chat? chat, bool created, byte[]? key)> CreatePrivateChat(
            long peerUserId, string passphrase, GlobalParam globalParam)
        {
            try
            {
                var salt = PrivateChatCrypto.GenerateSalt();
                var key = PrivateChatCrypto.DeriveKey(passphrase, salt);
                var verifier = PrivateChatCrypto.ComputeVerifier(key);

                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    var response = await MessagesAC!.CreatePrivateChatAsync(new Proto.Messages.CreatePrivateChatRequest
                    {
                        PeerUserId = peerUserId,
                        KdfSalt = ByteString.CopyFrom(salt),
                        PassphraseVerifier = ByteString.CopyFrom(verifier)
                    });

                    return ((ErrorReturner, Proto.Messages.Chat?, bool, byte[]?))(
                        new ErrorReturner(true), response.Chat, response.Created, key);
                }, globalParam);
            }
            catch (BarkFluff.Shared.Exceptions.Identity.UserNotFoundException)
            {
                return (new ErrorReturner(false, "Пользователь не найден"), null, false, null);
            }
            catch (Exception)
            {
                return (new ErrorReturner(false, "Ошибка создания приватного чата"), null, false, null);
            }
        }

        /// <summary>
        /// Принять приглашение в приватный чат. Кодовая фраза проверяется локально по verifier'у
        /// из инвайта — на сервер запрос уходит только после успешной проверки.
        /// </summary>
        public async Task<(ErrorReturner error, Proto.Messages.Chat? chat, byte[]? key)> AcceptPrivateChat(
            string chatId, string passphrase, byte[] kdfSalt, byte[] passphraseVerifier, GlobalParam globalParam)
        {
            try
            {
                var key = PrivateChatCrypto.DeriveKey(passphrase, kdfSalt);
                if (!PrivateChatCrypto.ValidateVerifier(key, passphraseVerifier))
                    return (new ErrorReturner(false, "Неверная кодовая фраза"), null, null);

                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    var response = await MessagesAC!.AcceptPrivateChatAsync(new Proto.Messages.AcceptPrivateChatRequest
                    {
                        ChatId = chatId
                    });

                    return ((ErrorReturner, Proto.Messages.Chat?, byte[]?))(new ErrorReturner(true), response.Chat, key);
                }, globalParam);
            }
            catch (BarkFluff.Shared.Exceptions.Messages.PrivateChatAlreadyAcceptedException)
            {
                return (new ErrorReturner(false, "Приглашение уже принято"), null, null);
            }
            catch (BarkFluff.Shared.Exceptions.Messages.PrivateChatInviteNotFoundException)
            {
                return (new ErrorReturner(false, "Приглашение не найдено"), null, null);
            }
            catch (Exception)
            {
                return (new ErrorReturner(false, "Ошибка присоединения к приватному чату"), null, null);
            }
        }

        /// <summary>
        /// Отклонить приглашение в приватный чат.
        /// </summary>
        public async Task<ErrorReturner> RejectPrivateChat(string chatId, GlobalParam globalParam)
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    await MessagesAC!.RejectPrivateChatAsync(new Proto.Messages.RejectPrivateChatRequest { ChatId = chatId });
                    return new ErrorReturner(true);
                }, globalParam);
            }
            catch (BarkFluff.Shared.Exceptions.Messages.PrivateChatInviteNotFoundException)
            {
                return new ErrorReturner(false, "Приглашение не найдено");
            }
            catch (Exception)
            {
                return new ErrorReturner(false, "Ошибка отклонения приглашения");
            }
        }

        /// <summary>
        /// Восстановить ключ уже существующего чата по кодовой фразе: salt и verifier
        /// приходят в самом <c>Chat</c> из ListChats. Сетевых вызовов не делает.
        /// Возвращает null, если фраза не подошла.
        /// </summary>
        public static byte[]? UnlockChat(Proto.Messages.Chat chat, string passphrase)
        {
            var key = PrivateChatCrypto.DeriveKey(passphrase, chat.KdfSalt.ToByteArray());
            return PrivateChatCrypto.ValidateVerifier(key, chat.PassphraseVerifier.ToByteArray()) ? key : null;
        }

        /// <summary>
        /// Зашифровать и отправить текст в приватный чат.
        /// </summary>
        public async Task<(ErrorReturner error, PrivateMessageModel? message)> SendPrivateMessage(
            string chatId, string text, byte[] key, GlobalParam globalParam)
        {
            try
            {
                var aad = PrivateChatCrypto.PrivateChatAad(chatId);
                var (ciphertext, nonce) = PrivateChatCrypto.Encrypt(Encoding.UTF8.GetBytes(text), key, aad);

                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    var response = await MessagesAC!.SendPrivateMessageAsync(new Proto.Messages.SendPrivateMessageRequest
                    {
                        ChatId = chatId,
                        Ciphertext = ByteString.CopyFrom(ciphertext),
                        Nonce = ByteString.CopyFrom(nonce),
                        AssociatedData = ByteString.CopyFrom(aad)
                    });

                    return ((ErrorReturner, PrivateMessageModel?))(new ErrorReturner(true), DecryptMessage(response.Message, key));
                }, globalParam);
            }
            catch (BarkFluff.Shared.Exceptions.Messages.ChatNotPrivateException)
            {
                return (new ErrorReturner(false, "Чат не является приватным"), null);
            }
            catch (BarkFluff.Shared.Exceptions.Messages.InvalidEncryptedPayloadException)
            {
                return (new ErrorReturner(false, "Сервер отклонил зашифрованное сообщение"), null);
            }
            catch (BarkFluff.Shared.Exceptions.Messages.NoAccessToChatException)
            {
                return (new ErrorReturner(false, "Нет доступа к чату"), null);
            }
            catch (Exception)
            {
                return (new ErrorReturner(false, "Ошибка отправки сообщения"), null);
            }
        }

        /// <summary>
        /// Страница сообщений приватного чата, расшифрованная на клиенте.
        /// Сообщения, которые расшифровать не удалось, возвращаются с DecryptionFailed=true.
        /// </summary>
        public async Task<(ErrorReturner error, List<PrivateMessageModel>? messages)> ListPrivateMessages(
            string chatId,
            byte[] key,
            GlobalParam globalParam,
            long fromMessageId = 0,
            int offsetBefore = DefaultPageSize,
            int offsetAfter = 0)
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    var response = await MessagesAC!.ListPrivateMessagesAsync(new Proto.Messages.ListPrivateMessagesRequest
                    {
                        ChatId = chatId,
                        FromMessageId = fromMessageId,
                        OffsetBefore = offsetBefore,
                        OffsetAfter = offsetAfter
                    });

                    return (new ErrorReturner(true), response.Messages.Select(m => DecryptMessage(m, key)).ToList());
                }, globalParam);
            }
            catch (BarkFluff.Shared.Exceptions.Messages.ChatNotPrivateException)
            {
                return (new ErrorReturner(false, "Чат не является приватным"), null);
            }
            catch (BarkFluff.Shared.Exceptions.Messages.NoAccessToChatException)
            {
                return (new ErrorReturner(false, "Нет доступа к чату"), null);
            }
            catch (Exception)
            {
                return (new ErrorReturner(false, "Ошибка получения сообщений"), null);
            }
        }

        /// <summary>
        /// Отредактировать своё сообщение приватного чата: шифруется заново, с новым nonce.
        /// </summary>
        public async Task<(ErrorReturner error, PrivateMessageModel? message)> EditPrivateMessage(
            string chatId, long messageId, string text, byte[] key, GlobalParam globalParam)
        {
            try
            {
                var aad = PrivateChatCrypto.PrivateChatAad(chatId);
                var (ciphertext, nonce) = PrivateChatCrypto.Encrypt(Encoding.UTF8.GetBytes(text), key, aad);

                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    var response = await MessagesAC!.EditPrivateMessageAsync(new Proto.Messages.EditPrivateMessageRequest
                    {
                        MessageId = messageId,
                        Ciphertext = ByteString.CopyFrom(ciphertext),
                        Nonce = ByteString.CopyFrom(nonce),
                        AssociatedData = ByteString.CopyFrom(aad)
                    });

                    return ((ErrorReturner, PrivateMessageModel?))(new ErrorReturner(true), DecryptMessage(response.Message, key));
                }, globalParam);
            }
            catch (BarkFluff.Shared.Exceptions.Messages.EncryptedMessageNotFoundException)
            {
                return (new ErrorReturner(false, "Сообщение не найдено"), null);
            }
            catch (BarkFluff.Shared.Exceptions.Messages.NoPermissionException)
            {
                return (new ErrorReturner(false, "Редактировать можно только свои сообщения"), null);
            }
            catch (BarkFluff.Shared.Exceptions.Messages.InvalidEncryptedPayloadException)
            {
                return (new ErrorReturner(false, "Сервер отклонил зашифрованное сообщение"), null);
            }
            catch (Exception)
            {
                return (new ErrorReturner(false, "Ошибка редактирования сообщения"), null);
            }
        }

        /// <summary>
        /// Удалить своё сообщение приватного чата (soft-delete, шифротекст очищается сервером).
        /// </summary>
        public async Task<ErrorReturner> DeletePrivateMessage(long messageId, GlobalParam globalParam)
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    await MessagesAC!.DeletePrivateMessageAsync(new Proto.Messages.DeletePrivateMessageRequest
                    {
                        MessageId = messageId
                    });
                    return new ErrorReturner(true);
                }, globalParam);
            }
            catch (BarkFluff.Shared.Exceptions.Messages.EncryptedMessageNotFoundException)
            {
                return new ErrorReturner(false, "Сообщение не найдено");
            }
            catch (BarkFluff.Shared.Exceptions.Messages.NoPermissionException)
            {
                return new ErrorReturner(false, "Удалять можно только свои сообщения");
            }
            catch (Exception)
            {
                return new ErrorReturner(false, "Ошибка удаления сообщения");
            }
        }

        /// <summary>
        /// Отметить сообщения приватного чата прочитанными до указанного включительно.
        /// </summary>
        public async Task<ErrorReturner> MarkPrivateMessagesAsRead(string chatId, long lastReadMessageId, GlobalParam globalParam)
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    await MessagesAC!.MarkPrivateMessagesAsReadAsync(new Proto.Messages.MarkPrivateMessagesAsReadRequest
                    {
                        ChatId = chatId,
                        LastReadMessageId = lastReadMessageId
                    });
                    return new ErrorReturner(true);
                }, globalParam);
            }
            catch (BarkFluff.Shared.Exceptions.Messages.NoAccessToChatException)
            {
                return new ErrorReturner(false, "Нет доступа к чату");
            }
            catch (Exception)
            {
                return new ErrorReturner(false, "Ошибка отметки о прочтении");
            }
        }

        /// <summary>
        /// Расшифровать сообщение приватного чата — в том числе пришедшее из стрима Updates.
        /// </summary>
        public static PrivateMessageModel DecryptMessage(Proto.Shared.EncryptedMessage message, byte[] key)
        {
            var model = new PrivateMessageModel
            {
                MessageId = message.Id,
                ChatId = message.ChatId,
                SenderId = message.SenderId,
                SenderDeviceId = message.SenderDeviceId,
                SentAt = message.SentAt,
                IsEdited = message.IsEdited,
                EditedAt = message.EditedAt,
                IsDeleted = message.IsDeleted,
            };

            // У удалённого сообщения сервер очищает шифротекст — расшифровывать нечего.
            if (message.IsDeleted || message.Ciphertext.IsEmpty)
                return model;

            // AAD берём из самого сообщения: отправитель мог привязать шифротекст
            // к дополнительному контексту. Пустой AAD => стандартный для чата.
            var aad = message.AssociatedData.IsEmpty
                ? PrivateChatCrypto.PrivateChatAad(message.ChatId)
                : message.AssociatedData.ToByteArray();

            try
            {
                var plaintext = PrivateChatCrypto.Decrypt(
                    message.Ciphertext.ToByteArray(),
                    message.Nonce.ToByteArray(),
                    key,
                    aad);

                model.Text = Encoding.UTF8.GetString(plaintext);
            }
            catch (CryptographicException)
            {
                model.DecryptionFailed = true;
            }
            catch (ArgumentException)
            {
                // Битые длины ключа/nonce в пришедшем сообщении — тоже неудачная расшифровка,
                // а не повод ронять весь список.
                model.DecryptionFailed = true;
            }

            return model;
        }
    }
}
