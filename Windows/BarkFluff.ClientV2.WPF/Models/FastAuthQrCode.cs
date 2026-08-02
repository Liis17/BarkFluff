namespace BarkFluff.ClientV2.WPF.Models;

public sealed record FastAuthQrCode(string Base64Png, DateTimeOffset ExpiresAt);
