namespace BarkFluff.Files.Exceptions;

public class FileAlreadyUploadedException : Exception
{
    public FileAlreadyUploadedException() : base() { }

    public FileAlreadyUploadedException(string message) : base(message) { }

    public FileAlreadyUploadedException(string message, Exception innerException)
        : base(message, innerException) { }
}
