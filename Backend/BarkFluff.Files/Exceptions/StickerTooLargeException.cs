namespace BarkFluff.Files.Exceptions;

public class StickerTooLargeException : Exception
{
    public StickerTooLargeException() : base("Размер стикера превышает лимит 12 МБ") { }
}
