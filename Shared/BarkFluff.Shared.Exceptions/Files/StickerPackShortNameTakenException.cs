namespace BarkFluff.Shared.Exceptions.Files;

public class StickerPackShortNameTakenException : BaseGrpcException
{
    public override string ErrorCode => "D8F63944-5920-46FC-8556-6438465F574A";

    public override string ErrorMessage => "Короткое имя набора уже занято";
}
