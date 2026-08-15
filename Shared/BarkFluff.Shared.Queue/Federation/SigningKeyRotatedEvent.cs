namespace BarkFluff.Shared.Queue.Federation;

// Ротация ключа подписи XFed (масштабирование, docs/scaling/federation.md). Publisher — Federation
// (FederationInternalApiHandler.RotateSigningKey); consumer — SigningKeyRotatedConsumer на каждом
// инстансе (fan-out): перезагружает ActiveSigningKeyCache и WellKnown-документ, чтобы исходящие
// подписи и well-known переключились на новый ключ без рестарта инстанса.
public class SigningKeyRotatedEvent
{
    public string NewKeyId { get; set; } = string.Empty;
}
