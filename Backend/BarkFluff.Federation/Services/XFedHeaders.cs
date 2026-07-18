namespace BarkFluff.Federation.Services;

// docs/rearch/02-trust-and-certs.md, "Подпись каждого S2S-запроса" (по образцу MetadataKeys в
// Shared/BarkFluff.Shared.Auth) — заголовки специфичны для Federation, живут только здесь.
public static class XFedHeaders
{
    public const string Origin = "x-bf-origin";
    public const string Destination = "x-bf-destination";
    public const string Timestamp = "x-bf-timestamp";
    public const string KeyId = "x-bf-key-id";
    public const string Signature = "x-bf-signature";
}
