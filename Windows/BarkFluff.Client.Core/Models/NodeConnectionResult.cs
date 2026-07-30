namespace BarkFluff.Client.Core.Models;

public sealed record NodeConnectionResult(NodeConnection? Connection, string? ErrorResourceKey)
{
    public bool IsSuccess => Connection is not null;

    public static NodeConnectionResult Success(NodeConnection connection) => new(connection, null);

    public static NodeConnectionResult Failure(string errorResourceKey) => new(null, errorResourceKey);
}
