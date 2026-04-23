namespace Barkfluff.Developers.Domain;

public class ErrorCodeEntry
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string ExceptionName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
}
