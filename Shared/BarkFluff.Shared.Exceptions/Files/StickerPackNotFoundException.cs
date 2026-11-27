namespace BarkFluff.Shared.Exceptions.Files;

public class StickerPackNotFoundException : BaseGrpcException
{
    public override string ErrorCode => "498358C2-D22B-4A96-B4E8-0EEC4ECBD1D7";

    public override string ErrorMessage => "Набор стикеров не найден";
}
