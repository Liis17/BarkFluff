using System.Text.RegularExpressions;

namespace BarkFluff.Files.Helpers;

/// <summary>Короткое имя набора стикеров для ссылки /addstickers/&lt;имя&gt;. Сравнивается без учёта регистра.</summary>
public static partial class StickerPackShortName
{
    [GeneratedRegex("^[a-z][a-z0-9_]{2,63}$")]
    private static partial Regex ValidPattern();

    public static string Normalize(string value) => value.Trim().ToLowerInvariant();

    public static bool IsValid(string normalized) => ValidPattern().IsMatch(normalized);

    public static string Generate(Guid packId) => "pack_" + packId.ToString("N")[..12];
}
