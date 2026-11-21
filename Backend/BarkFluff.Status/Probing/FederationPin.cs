using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace BarkFluff.Status.Probing;

/// <summary>
/// SPKI-пин для Federation: тот же контракт, что у S2SChannelFactory (Base64(SHA256(SubjectPublicKeyInfo))).
/// Пин заменяет проверку цепочки только для Federation и только когда задан; несовпадение — отказ (fail-closed).
/// </summary>
public static class FederationPin
{
    public static bool Accepts(string pin, X509Certificate? certificate)
    {
        if (certificate is null)
            return false;

        using var certificate2 = certificate as X509Certificate2 ?? new X509Certificate2(certificate);
        var fingerprint = SHA256.HashData(certificate2.PublicKey.ExportSubjectPublicKeyInfo());
        var expected = Convert.FromBase64String(pin);
        return CryptographicOperations.FixedTimeEquals(fingerprint, expected);
    }
}
