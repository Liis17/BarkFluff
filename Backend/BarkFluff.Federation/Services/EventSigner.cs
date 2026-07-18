using BarkFluff.Federation.Domain.Entities;
using BarkFluff.Federation.Services;
using BarkFluff.Proto.Federation;

using Google.Protobuf;

namespace BarkFluff.Federation.Services;

// Подпись FederationEvent ключом origin-ноды (этап 2.2, docs/rearch/02-trust-and-certs.md
// раздел «Подпись FederationEvent», docs/rearch/04 §"Outbox").
//
// Канонизация: wire-байты события с очищенными origin_signature/origin_key_id. C#-реализация
// protobuf сериализует поля в порядке возрастания номеров, поэтому сериализации отправителя и
// получателя совпадают. Спецификация протокола (Фаза 6) зафиксирует это требование.
//
// Подпись — Ed25519 через BouncyCastle (SigningKeyService.SignRaw/Verify), тот же стек, что в XFed.
public static class EventSigner
{
    /// <summary>
    /// Подписать событие активным ключом ноды. Мутирует event: проставляет origin_signature/origin_key_id.
    /// </summary>
    public static void Sign(FederationEvent evt, FederationSigningKey key)
    {
        evt.OriginSignature = ByteString.Empty;
        evt.OriginKeyId = string.Empty;
        var wireBytes = evt.ToByteArray();
        var signature = SigningKeyService.SignRaw(key.PrivateKeySeed, wireBytes);
        evt.OriginSignature = ByteString.CopyFrom(signature);
        evt.OriginKeyId = key.KeyId;
    }

    /// <summary>
    /// Проверить подпись входящего события публичным ключом origin.
    /// </summary>
    public static bool Verify(FederationEvent evt, byte[] originPublicKey)
    {
        if (evt.OriginSignature.IsEmpty || string.IsNullOrEmpty(evt.OriginKeyId))
            return false;

        var signature = evt.OriginSignature.Span;
        var verifier = (FederationEvent)evt.Clone();
        verifier.OriginSignature = ByteString.Empty;
        verifier.OriginKeyId = string.Empty;
        var wireBytes = verifier.ToByteArray();

        return SigningKeyService.Verify(originPublicKey, wireBytes, signature.ToArray());
    }
}
