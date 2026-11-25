using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using BarkFluff.Client.Core.Services.SecretChats;
using Xunit;

namespace BarkFluff.Client.WinUI.Tests.SignalInterop;

[Collection("Signal shim")]
public sealed class SignalProtocolTests
{
    private const string Peer = "node-device";
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task PqxdhInteroperatesWithOfficialLibsignal(bool windowsInitiates, bool lastResort)
    {
        using var node = StartPeer();
        var stderr = node.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            var device = SignalProtocol.CreateDevice();
            var state = device.State;
            var generation = device.Generation;

            void Advance<T>(SignalResult<T> result)
            {
                Assert.Equal(generation + 1, result.Generation);
                state = result.State;
                generation = result.Generation;
            }

            async Task<T> Call<T>(object command)
            {
                await node.StandardInput.WriteLineAsync(JsonSerializer.Serialize(command, _json).AsMemory(), timeout.Token);
                var line = await node.StandardOutput.ReadLineAsync(timeout.Token);
                if (line == null)
                    throw new InvalidOperationException($"Official libsignal peer failed. {await stderr}");
                return JsonSerializer.Deserialize<T>(line, _json)!;
            }

            SignalCiphertext WindowsEncrypt(byte[] plaintext)
            {
                var result = SignalProtocol.Encrypt(state, generation, Peer, plaintext);
                Advance(result);
                return result.Output;
            }

            byte[] WindowsDecrypt(SignalCiphertext message)
            {
                var result = SignalProtocol.Decrypt(state, generation, Peer, message.Kind, message.Ciphertext);
                Advance(result);
                return result.Output;
            }

            Task<SignalCiphertext> NodeEncrypt(byte[] plaintext) =>
                Call<SignalCiphertext>(new { operation = "encrypt", plaintext });

            Task<byte[]> NodeDecrypt(SignalCiphertext message) =>
                Call<byte[]>(new { operation = "decrypt", message });

            if (windowsInitiates)
            {
                var bundle = await Call<SignalPreKeyBundle>(new { operation = "bundle" });
                Advance(SignalProtocol.ProcessBundle(state, generation, Peer, bundle));
                var first = WindowsEncrypt([1]);
                Assert.Equal(SignalMessageKind.PreKey, first.Kind);
                Assert.Equal(new byte[] { 1 }, await NodeDecrypt(first));

                var reply = await NodeEncrypt([2]);
                Assert.Equal(SignalMessageKind.Signal, reply.Kind);
                Assert.Equal(new byte[] { 2 }, WindowsDecrypt(reply));
            }
            else
            {
                var oneTime = lastResort ? null : SignalProtocol.GeneratePreKey(state, generation, 1);
                if (oneTime != null)
                    Advance(oneTime);
                var signed = SignalProtocol.GenerateSignedPreKey(state, generation, 2);
                Advance(signed);
                var kyber = SignalProtocol.GenerateKyberPreKey(state, generation, 3, lastResort);
                Advance(kyber);
                var bundle = new SignalPreKeyBundle(device.Output.RegistrationId, device.Output.IdentityKey,
                    oneTime?.Output, signed.Output, kyber.Output);
                Assert.True(await Call<bool>(new { operation = "processBundle", bundle }));
                var first = await NodeEncrypt([1]);
                Assert.Equal(SignalMessageKind.PreKey, first.Kind);
                Assert.Equal(4, first.Ciphertext[0] >> 4);
                Assert.Equal(new byte[] { 1 }, WindowsDecrypt(first));

                if (!lastResort)
                {
                    Assert.Equal(SignalError.Protocol, Assert.Throws<SignalProtocolException>(() =>
                        SignalProtocol.GetKyberPreKey(state, generation, kyber.Output.Id)).Error);
                }

                if (lastResort)
                {
                    // Убираем ratchet, чтобы повтор дошёл до проверки base key хранилищем.
                    var deleted = SignalProtocol.DeleteSession(state, generation, Peer);
                    Assert.Equal(SignalError.SessionNotFound, Assert.Throws<SignalProtocolException>(() =>
                        SignalProtocol.Encrypt(deleted.State, deleted.Generation, Peer, [2])).Error);
                    Assert.Equal(SignalError.DuplicateMessage, Assert.Throws<SignalProtocolException>(() =>
                        SignalProtocol.Decrypt(deleted.State, deleted.Generation, Peer, first.Kind, first.Ciphertext)).Error);
                }

                var reply = WindowsEncrypt([2]);
                Assert.Equal(SignalMessageKind.Signal, reply.Kind);
                Assert.Equal(new byte[] { 2 }, await NodeDecrypt(reply));
            }

            var windowsEarlier = WindowsEncrypt([3, 0, 255]);
            var windowsLater = WindowsEncrypt([4]);
            Assert.Equal(SignalMessageKind.Signal, windowsEarlier.Kind);
            Assert.Equal(new byte[] { 4 }, await NodeDecrypt(windowsLater));
            Assert.Equal(new byte[] { 3, 0, 255 }, await NodeDecrypt(windowsEarlier));

            var nodeEarlier = await NodeEncrypt([5]);
            var nodeLater = await NodeEncrypt([6]);
            Assert.Equal(SignalMessageKind.Signal, nodeEarlier.Kind);
            Assert.Equal(new byte[] { 6 }, WindowsDecrypt(nodeLater));
            Assert.Equal(new byte[] { 5 }, WindowsDecrypt(nodeEarlier));
            Assert.Equal(SignalError.DuplicateMessage,
                Assert.Throws<SignalProtocolException>(() => WindowsDecrypt(nodeEarlier)).Error);
            node.StandardInput.Close();
            await node.WaitForExitAsync(timeout.Token);
            Assert.Equal(0, node.ExitCode);
        }
        finally
        {
            if (!node.HasExited)
                node.Kill(entireProcessTree: true);
        }
    }

    [Fact]
    public void InvalidInputsReturnCodesAndLeaveTheSnapshotUsable()
    {
        var device = SignalProtocol.CreateDevice();
        Assert.InRange(device.Output.RegistrationId, 1u, 16_380u);
        Assert.Equal(33, device.Output.IdentityKey.Length);
        Assert.Equal(SignalError.InvalidInput,
            Assert.Throws<SignalProtocolException>(() => SignalProtocol.GeneratePreKey([1], 0, 1)).Error);
        Assert.Equal(SignalError.SessionNotFound,
            Assert.Throws<SignalProtocolException>(() =>
                SignalProtocol.Encrypt(device.State, device.Generation, Peer, [1])).Error);
        var preKey = SignalProtocol.GeneratePreKey(device.State, device.Generation, 1);
        Assert.Equal(SignalError.InvalidInput,
            Assert.Throws<SignalProtocolException>(() =>
                SignalProtocol.GeneratePreKey(preKey.State, preKey.Generation, 1)).Error);
        Assert.Equal(2u, SignalProtocol.GeneratePreKey(preKey.State, preKey.Generation, 2).Output.Id);
    }

    [Fact]
    public void UnknownSnapshotVersionHasASeparateError()
    {
        Assert.Equal(SignalError.UnsupportedSnapshot, Assert.Throws<SignalProtocolException>(() =>
            SignalProtocol.GeneratePreKey("""{"version":2}"""u8, 0, 1)).Error);
    }

    [Fact]
    public void OversizedRequestIsRejectedBeforeEncryption()
    {
        var device = SignalProtocol.CreateDevice();
        Assert.Equal(SignalError.InvalidInput, Assert.Throws<SignalProtocolException>(() =>
            SignalProtocol.Encrypt(device.State, device.Generation, Peer, new byte[768 * 1024])).Error);
    }

    [Fact]
    public void SnapshotGenerationMustMatchExpectedGeneration()
    {
        var device = SignalProtocol.CreateDevice();
        Assert.Equal(1ul, device.Generation);
        var updated = SignalProtocol.GeneratePreKey(device.State, device.Generation, 1);
        Assert.Equal(device.Generation + 1, updated.Generation);

        Assert.Equal(SignalError.GenerationMismatch, Assert.Throws<SignalProtocolException>(() =>
            SignalProtocol.GeneratePreKey(device.State, updated.Generation, 2)).Error);
        Assert.Equal(SignalError.GenerationMismatch, Assert.Throws<SignalProtocolException>(() =>
            SignalProtocol.GeneratePreKey(updated.State, device.Generation, 2)).Error);
        Assert.Equal(updated.Generation + 1,
            SignalProtocol.GeneratePreKey(updated.State, updated.Generation, 2).Generation);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StoredPreKeyMetadataCanBeReadAndKeysCanBeRemoved(bool lastResort)
    {
        var device = SignalProtocol.CreateDevice();
        var signed = SignalProtocol.GenerateSignedPreKey(device.State, device.Generation, 1);
        var kyber = SignalProtocol.GenerateKyberPreKey(signed.State, signed.Generation, 2, lastResort);
        var readSigned = SignalProtocol.GetSignedPreKey(kyber.State, kyber.Generation, 1);
        var readKyber = SignalProtocol.GetKyberPreKey(kyber.State, kyber.Generation, 2);
        Assert.Equal(kyber.Generation, readSigned.Generation);
        Assert.Equal(kyber.Generation, readKyber.Generation);
        Assert.Equal(kyber.State, readKyber.State);
        AssertSameKey(signed.Output, readSigned.Output);
        AssertSameKey(kyber.Output, readKyber.Output);

        var withoutSigned = SignalProtocol.RemoveSignedPreKey(kyber.State, kyber.Generation, 1);
        Assert.Equal(kyber.Generation + 1, withoutSigned.Generation);
        Assert.Equal(SignalError.Protocol, Assert.Throws<SignalProtocolException>(() =>
            SignalProtocol.GetSignedPreKey(withoutSigned.State, withoutSigned.Generation, 1)).Error);
        AssertSameKey(kyber.Output,
            SignalProtocol.GetKyberPreKey(withoutSigned.State, withoutSigned.Generation, 2).Output);
        var withoutKyber = SignalProtocol.RemoveKyberPreKey(withoutSigned.State, withoutSigned.Generation, 2);
        Assert.Equal(withoutSigned.Generation + 1, withoutKyber.Generation);
        Assert.Equal(SignalError.Protocol, Assert.Throws<SignalProtocolException>(() =>
            SignalProtocol.GetKyberPreKey(withoutKyber.State, withoutKyber.Generation, 2)).Error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TamperedPreKeySignatureIsRejected(bool tamperKyber)
    {
        var alice = SignalProtocol.CreateDevice();
        var bob = SignalProtocol.CreateDevice();
        var signed = SignalProtocol.GenerateSignedPreKey(bob.State, bob.Generation, 1).Output;
        var kyber = SignalProtocol.GenerateKyberPreKey(bob.State, bob.Generation, 2, true).Output;
        var bundle = new SignalPreKeyBundle(bob.Output.RegistrationId, bob.Output.IdentityKey, null, signed, kyber);
        var signature = (byte[])(tamperKyber ? kyber.Signature : signed.Signature).Clone();
        signature[0] ^= 1;
        var tampered = tamperKyber
            ? bundle with { KyberPreKey = kyber with { Signature = signature } }
            : bundle with { SignedPreKey = signed with { Signature = signature } };

        Assert.Equal(SignalError.Protocol, Assert.Throws<SignalProtocolException>(() =>
            SignalProtocol.ProcessBundle(alice.State, alice.Generation, Peer, tampered)).Error);
        Assert.Equal(alice.Generation + 1, SignalProtocol.ProcessBundle(alice.State, alice.Generation, Peer, bundle).Generation);
    }

    [Fact]
    public void SafeHandleReleasesNativeResponsesWithoutWaitingForGc()
    {
        for (var i = 0; i < 32; i++)
        {
            var device = SignalProtocol.CreateDevice();
            SignalProtocol.GeneratePreKey(device.State, device.Generation, 1);
            Assert.Equal((nuint)0, SignalProtocol.GetLiveResponseCount());
        }
    }

    private static void AssertSameKey(SignalSignedPreKey expected, SignalSignedPreKey actual)
    {
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.PublicKey, actual.PublicKey);
        Assert.Equal(expected.Signature, actual.Signature);
        Assert.Equal(expected.Timestamp, actual.Timestamp);
        Assert.True(actual.Timestamp > 0);
    }

    private static Process StartPeer([CallerFilePath] string testFile = "")
    {
        var startInfo = new ProcessStartInfo("node")
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add(Path.Combine(Path.GetDirectoryName(testFile)!, "peer.mjs"));
        return Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start the Node peer.");
    }
}
