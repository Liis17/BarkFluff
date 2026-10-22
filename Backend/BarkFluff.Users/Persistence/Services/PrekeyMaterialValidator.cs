using Grpc.Core;

namespace BarkFluff.Users.Persistence.Services;

internal static class PrekeyMaterialValidator
{
    private const int MaxSerializedKeyLength = 4096;
    private const int MaxSignatureLength = 512;
    private const int MaxPrekeysPerRequest = 1000;

    public static void ValidateKey(string name, byte[] value)
    {
        if (value.Length is 0 or > MaxSerializedKeyLength)
        {
            throw new PrekeyException(StatusCode.InvalidArgument, $"{name} должен быть сериализованным ключом длиной от 1 до {MaxSerializedKeyLength} байт");
        }
    }

    public static void ValidateSignature(string name, byte[] value)
    {
        if (value.Length is 0 or > MaxSignatureLength)
        {
            throw new PrekeyException(StatusCode.InvalidArgument, $"{name} должен содержать от 1 до {MaxSignatureLength} байт");
        }
    }

    public static void ValidateBatchCount(string name, int count)
    {
        if (count > MaxPrekeysPerRequest)
        {
            throw new PrekeyException(StatusCode.InvalidArgument, $"{name} не должен превышать {MaxPrekeysPerRequest} ключей за запрос");
        }
    }
}
