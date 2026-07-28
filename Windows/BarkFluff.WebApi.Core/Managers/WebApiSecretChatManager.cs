using BarkFluff.WebApi.Core.MessengerData;

using Google.Protobuf;

namespace BarkFluff.WebApi.Core.Managers
{
    /// <summary>
    /// Транспорт секретных чатов. Шифрование libsignal не реализовано: envelope
    /// передаётся серверу и обратно без изменений.
    /// </summary>
    internal class WebApiSecretChatManager : WebApiBase
    {
        private readonly WebApi _webApi;

        public WebApiSecretChatManager(WebApi webApi) : base(webApi)
        {
            _webApi = webApi;
        }

        /// <summary>
        /// Отправить инвайт секретного чата. Крипта libsignal не реализована,
        /// initialEnvelope прокидывается как есть.
        /// </summary>
        public async Task<(ErrorReturner error, Proto.Messages.SendSecretChatInviteResponse? response)> SendSecretChatInvite(
            long recipientUserId, string recipientDeviceId, byte[] initialEnvelope, GlobalParam globalParam)
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    var response = await MessagesAC!.SendSecretChatInviteAsync(new Proto.Messages.SendSecretChatInviteRequest
                    {
                        RecipientUserId = recipientUserId,
                        RecipientDeviceId = recipientDeviceId,
                        InitialEnvelope = ByteString.CopyFrom(initialEnvelope)
                    });
                    return ((ErrorReturner, Proto.Messages.SendSecretChatInviteResponse?))(new ErrorReturner(true), response);
                }, globalParam);
            }
            catch (Exception)
            {
                return (new ErrorReturner(false, "Ошибка отправки приглашения в секретный чат"), null);
            }
        }

        /// <summary>
        /// Принять инвайт секретного чата. Крипта libsignal не реализована,
        /// responseEnvelope прокидывается как есть.
        /// </summary>
        public async Task<ErrorReturner> AcceptSecretChatInvite(string inviteId, byte[] responseEnvelope, GlobalParam globalParam)
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    await MessagesAC!.AcceptSecretChatInviteAsync(new Proto.Messages.AcceptSecretChatInviteRequest
                    {
                        InviteId = inviteId,
                        ResponseEnvelope = ByteString.CopyFrom(responseEnvelope)
                    });
                    return new ErrorReturner(true);
                }, globalParam);
            }
            catch (Exception)
            {
                return new ErrorReturner(false, "Ошибка принятия приглашения в секретный чат");
            }
        }

        /// <summary>
        /// Отклонить инвайт секретного чата. Крипта libsignal не реализована.
        /// </summary>
        public async Task<ErrorReturner> RejectSecretChatInvite(string inviteId, GlobalParam globalParam)
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    await MessagesAC!.RejectSecretChatInviteAsync(new Proto.Messages.RejectSecretChatInviteRequest { InviteId = inviteId });
                    return new ErrorReturner(true);
                }, globalParam);
            }
            catch (Exception)
            {
                return new ErrorReturner(false, "Ошибка отклонения приглашения в секретный чат");
            }
        }

        /// <summary>
        /// Отправить секретное сообщение. Крипта libsignal не реализована,
        /// envelope прокидывается как есть.
        /// </summary>
        public async Task<(ErrorReturner error, Proto.Messages.SendSecretMessageResponse? response)> SendSecretMessage(
            long recipientUserId, string recipientDeviceId, byte[] envelope, GlobalParam globalParam)
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    var response = await MessagesAC!.SendSecretMessageAsync(new Proto.Messages.SendSecretMessageRequest
                    {
                        RecipientUserId = recipientUserId,
                        RecipientDeviceId = recipientDeviceId,
                        Envelope = ByteString.CopyFrom(envelope)
                    });
                    return ((ErrorReturner, Proto.Messages.SendSecretMessageResponse?))(new ErrorReturner(true), response);
                }, globalParam);
            }
            catch (Exception)
            {
                return (new ErrorReturner(false, "Ошибка отправки секретного сообщения"), null);
            }
        }

        /// <summary>
        /// Подтвердить доставку секретного сообщения. Крипта libsignal не реализована.
        /// </summary>
        public async Task<ErrorReturner> AckSecretMessage(string messageId, GlobalParam globalParam)
        {
            try
            {
                return await _webApi.TokenManager.SafeCallAsync(async () =>
                {
                    await MessagesAC!.AckSecretMessageAsync(new Proto.Messages.AckSecretMessageRequest { MessageId = messageId });
                    return new ErrorReturner(true);
                }, globalParam);
            }
            catch (Exception)
            {
                return new ErrorReturner(false, "Ошибка подтверждения доставки секретного сообщения");
            }
        }
    }
}
