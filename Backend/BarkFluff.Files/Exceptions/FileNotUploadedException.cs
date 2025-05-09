namespace BarkFluff.Files.Exceptions;

public class FileNotUploadedException : Exception
{
    public FileNotUploadedException(string message) : base(message)
    {
    }
}
