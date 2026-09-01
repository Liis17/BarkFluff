using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace BarkFluff.Client.Core.Services;

public static class UpdateHttpClientFactory
{
    private static readonly string CertificateRelativePath = Path.Combine(
        "Resources",
        "Update",
        "storage-ca.pem");

    public static HttpClient Create()
    {
        var certificatePath = Path.Combine(AppContext.BaseDirectory, CertificateRelativePath);
        if (!File.Exists(certificatePath))
        {
            return new HttpClient();
        }

        var trustedRoots = new X509Certificate2Collection();
        trustedRoots.ImportFromPem(File.ReadAllText(certificatePath));
        if (trustedRoots.Count == 0)
        {
            throw new InvalidDataException($"CA-бандл обновлений пуст: {certificatePath}");
        }

        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, certificate, _, sslPolicyErrors) =>
            {
                if (certificate is null ||
                    (sslPolicyErrors & (SslPolicyErrors.RemoteCertificateNameMismatch |
                                        SslPolicyErrors.RemoteCertificateNotAvailable)) != 0)
                {
                    return false;
                }

                using var serverCertificate = new X509Certificate2(certificate);
                using var chain = new X509Chain();
                chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                chain.ChainPolicy.CustomTrustStore.AddRange(trustedRoots);
                chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                return chain.Build(serverCertificate);
            }
        };

        return new HttpClient(handler, disposeHandler: true);
    }
}
