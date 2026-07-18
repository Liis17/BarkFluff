using BarkFluff.Federation.Services;

using FluentAssertions;

using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;

namespace BarkFluff.Federation.Tests.Services;

public class SigningKeyServiceCryptoTests
{
    private static (byte[] Seed, byte[] Pub) GenerateKeyPair()
    {
        var random = new SecureRandom();
        var kpGen = new Ed25519KeyPairGenerator();
        kpGen.Init(new KeyGenerationParameters(random, 256));
        var kp = kpGen.GenerateKeyPair();
        var priv = (Ed25519PrivateKeyParameters)kp.Private;
        var pub = (Ed25519PublicKeyParameters)kp.Public;
        return (priv.GetEncoded(), pub.GetEncoded());
    }

    [Fact]
    public void SignRaw_Then_Verify_Roundtrip_Succeeds()
    {
        var (seed, pub) = GenerateKeyPair();
        var data = "canonical-string"u8.ToArray();

        var signature = SigningKeyService.SignRaw(seed, data);

        SigningKeyService.Verify(pub, data, signature).Should().BeTrue();
        seed.Should().HaveCount(32);
        pub.Should().HaveCount(32);
        signature.Should().HaveCount(64);
    }

    [Fact]
    public void Verify_TamperedSignature_Fails()
    {
        var (seed, pub) = GenerateKeyPair();
        var data = "canonical-string"u8.ToArray();

        var signature = SigningKeyService.SignRaw(seed, data);
        signature[0] ^= 0xFF;

        SigningKeyService.Verify(pub, data, signature).Should().BeFalse();
    }

    [Fact]
    public void Verify_TamperedData_Fails()
    {
        var (seed, pub) = GenerateKeyPair();
        var data = "canonical-string"u8.ToArray();

        var signature = SigningKeyService.SignRaw(seed, data);
        var tamperedData = "canonical-strinG"u8.ToArray();

        SigningKeyService.Verify(pub, tamperedData, signature).Should().BeFalse();
    }

    [Fact]
    public void Verify_WrongPublicKey_Fails()
    {
        var (seed, _) = GenerateKeyPair();
        var (_, otherPub) = GenerateKeyPair();
        var data = "canonical-string"u8.ToArray();

        var signature = SigningKeyService.SignRaw(seed, data);

        SigningKeyService.Verify(otherPub, data, signature).Should().BeFalse();
    }
}
