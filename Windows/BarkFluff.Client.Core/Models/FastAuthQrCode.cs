namespace BarkFluff.Client.Core.Models;

public sealed record FastAuthQrCode(string Base64Png, DateTimeOffset ExpiresAt);
