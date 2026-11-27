namespace BarkFluff.Shared.Exceptions.Files;

public class StickerPackShortNameNotValidException : BaseGrpcException
{
    public override string ErrorCode => "F65F0570-B8C4-459F-8484-B0E0AE5C0656";

    public override string ErrorMessage => "Короткое имя набора: латинская буква, затем 2–63 символа из латинских букв, цифр и _";
}
