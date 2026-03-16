namespace BarkFluff.Files.Exceptions;

public class StickerDimensionExceededException : Exception
{
    public StickerDimensionExceededException() : base("Разрешение стикера превышает 1024 пикселей") { }
}
