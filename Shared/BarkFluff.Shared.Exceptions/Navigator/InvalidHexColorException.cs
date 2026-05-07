namespace BarkFluff.Shared.Exceptions.Navigator;

public class InvalidHexColorException : BaseGrpcException
{
    public override string ErrorCode => "E1F2A3B4-5C6D-4E7F-8A9B-0C1D2E3F4A5B";
    public override string ErrorMessage => "Цвет имеет некорректный формат (ожидается #RRGGBB)";
}
