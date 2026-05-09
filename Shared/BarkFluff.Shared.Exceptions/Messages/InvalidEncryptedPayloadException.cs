namespace BarkFluff.Shared.Exceptions.Messages;

public class InvalidEncryptedPayloadException : BaseGrpcException
{
    public override string ErrorCode => "9C82E2A7-5E2C-49EA-B12E-A1F70E64D3C7";

    public override string ErrorMessage => "Некорректный шифрованный payload (ciphertext/nonce/AAD)";
}
