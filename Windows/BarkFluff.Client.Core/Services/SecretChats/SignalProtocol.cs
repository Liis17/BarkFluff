using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace BarkFluff.Client.Core.Services.SecretChats;

public sealed record SignalDevice(uint RegistrationId, byte[] IdentityKey);
public sealed record SignalPreKey(uint Id, byte[] PublicKey);
public sealed record SignalSignedPreKey(uint Id, byte[] PublicKey, byte[] Signature, ulong Timestamp = 0);

public sealed record SignalPreKeyBundle(
    uint RegistrationId,
    byte[] IdentityKey,
    SignalPreKey? PreKey,
    SignalSignedPreKey SignedPreKey,
    SignalSignedPreKey KyberPreKey);

public enum SignalMessageKind : byte
{
    Signal = 2,
    PreKey = 3
}

public sealed record SignalCiphertext(SignalMessageKind Kind, byte[] Ciphertext);

public enum SignalError
{
    InvalidInput = 1,
    Protocol = 2,
    NativePanic = 3,
    UntrustedIdentity = 4,
    DuplicateMessage = 5,
    SessionNotFound = 6,
    GenerationMismatch = 7,
    UnsupportedSnapshot = 8
}

public sealed class SignalProtocolException : Exception
{
    internal SignalProtocolException(SignalError error) : base($"Signal operation failed ({error}).")
    {
        Error = error;
    }

    public SignalError Error { get; }
}

// Снимок содержит закрытые ключи. Вызывающий код защищает его при хранении
// и атомарно сохраняет его, сверяя поколение с текущим в хранилище.
public sealed record SignalResult<T>(byte[] State, ulong Generation, T Output);

public static partial class SignalProtocol
{
    private const string LibraryName = "barkfluff_signal_shim";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static SignalResult<SignalDevice> CreateDevice() =>
        Execute<SignalDevice>([], 0, new { operation = "create" });

    public static SignalResult<SignalPreKey> GeneratePreKey(
        ReadOnlySpan<byte> state, ulong expectedGeneration, uint id) =>
        Execute<SignalPreKey>(state, expectedGeneration, new { operation = "generatePreKey", id });

    public static SignalResult<SignalSignedPreKey> GenerateSignedPreKey(
        ReadOnlySpan<byte> state, ulong expectedGeneration, uint id) =>
        Execute<SignalSignedPreKey>(state, expectedGeneration, new { operation = "generateSignedPreKey", id });

    public static SignalResult<SignalSignedPreKey> GenerateKyberPreKey(
        ReadOnlySpan<byte> state, ulong expectedGeneration, uint id, bool lastResort) =>
        Execute<SignalSignedPreKey>(state, expectedGeneration, new { operation = "generateKyberPreKey", id, lastResort });

    public static SignalResult<SignalSignedPreKey> GetSignedPreKey(
        ReadOnlySpan<byte> state, ulong expectedGeneration, uint id) =>
        Execute<SignalSignedPreKey>(state, expectedGeneration, new { operation = "getSignedPreKey", id });

    public static SignalResult<SignalSignedPreKey> GetKyberPreKey(
        ReadOnlySpan<byte> state, ulong expectedGeneration, uint id) =>
        Execute<SignalSignedPreKey>(state, expectedGeneration, new { operation = "getKyberPreKey", id });

    public static SignalResult<object?> RemoveSignedPreKey(ReadOnlySpan<byte> state, ulong expectedGeneration, uint id) =>
        Execute<object?>(state, expectedGeneration, new { operation = "removeSignedPreKey", id });

    public static SignalResult<object?> RemoveKyberPreKey(ReadOnlySpan<byte> state, ulong expectedGeneration, uint id) =>
        Execute<object?>(state, expectedGeneration, new { operation = "removeKyberPreKey", id });

    public static SignalResult<object?> DeleteSession(ReadOnlySpan<byte> state, ulong expectedGeneration, string peer) =>
        Execute<object?>(state, expectedGeneration, new { operation = "deleteSession", peer });

    public static SignalResult<object?> ProcessBundle(
        ReadOnlySpan<byte> state, ulong expectedGeneration, string peer, SignalPreKeyBundle bundle) =>
        Execute<object?>(state, expectedGeneration, new { operation = "processBundle", peer, bundle });

    public static SignalResult<SignalCiphertext> Encrypt(
        ReadOnlySpan<byte> state, ulong expectedGeneration, string peer, byte[] plaintext) =>
        Execute<SignalCiphertext>(state, expectedGeneration, new { operation = "encrypt", peer, plaintext });

    public static SignalResult<byte[]> Decrypt(
        ReadOnlySpan<byte> state, ulong expectedGeneration, string peer, SignalMessageKind kind, byte[] ciphertext) =>
        Execute<byte[]>(state, expectedGeneration, new { operation = "decrypt", peer, kind, ciphertext });

    private static unsafe SignalResult<T> Execute<T>(ReadOnlySpan<byte> state, ulong expectedGeneration, object operation)
    {
        var request = JsonSerializer.SerializeToUtf8Bytes(operation, Json);
        try
        {
            fixed (byte* stateData = state, requestData = request)
            {
                var error = ExecuteNative(stateData, (nuint)state.Length, expectedGeneration,
                    requestData, (nuint)request.Length, out var response, out var view);
                using (response)
                {
                    if (error != 0)
                        throw new SignalProtocolException(error);

                    var output = JsonSerializer.Deserialize<T>(
                        new ReadOnlySpan<byte>(view.Output, checked((int)view.OutputLength)), Json);
                    if (output is null && typeof(T) != typeof(object))
                        throw new SignalProtocolException(SignalError.Protocol);

                    var snapshot = new ReadOnlySpan<byte>(view.State, checked((int)view.StateLength)).ToArray();
                    return new SignalResult<T>(snapshot, view.Generation, output!);
                }
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(request);
        }
    }

    [LibraryImport(LibraryName, EntryPoint = "barkfluff_signal_execute")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe partial SignalError ExecuteNative(
        byte* state, nuint stateLength, ulong expectedGeneration, byte* request, nuint requestLength,
        out ResponseHandle response, out ResponseView view);

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct ResponseView
    {
        public ulong Generation;
        public byte* State;
        public nuint StateLength;
        public byte* Output;
        public nuint OutputLength;
    }

    [LibraryImport(LibraryName, EntryPoint = "barkfluff_signal_live_responses")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial nuint GetLiveResponseCount();

    [LibraryImport(LibraryName, EntryPoint = "barkfluff_signal_free")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial void FreeNative(nint response);

    private sealed class ResponseHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public ResponseHandle() : base(ownsHandle: true) { }

        protected override bool ReleaseHandle()
        {
            FreeNative(handle);
            return true;
        }
    }
}
