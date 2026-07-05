using System.Text.Json.Serialization;

namespace BarkFluff.Bots.Host.Http;

/// <summary>Ответы Bot REST API: {"ok":true,"result":...} / {"ok":false,"error_code":N,"description":"..."}</summary>
public static class BotApiResponse
{
    public static IResult Ok(object result)
        => Results.Json(new BotApiOkResponse { Result = result });

    public static IResult Error(int statusCode, string description)
        => Results.Json(
            new BotApiErrorResponse { ErrorCode = statusCode, Description = description },
            statusCode: statusCode);
}

public class BotApiOkResponse
{
    [JsonPropertyName("ok")]
    public bool Ok => true;

    [JsonPropertyName("result")]
    public object? Result { get; set; }
}

public class BotApiErrorResponse
{
    [JsonPropertyName("ok")]
    public bool Ok => false;

    [JsonPropertyName("error_code")]
    public int ErrorCode { get; set; }

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;
}
