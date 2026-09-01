using System.Globalization;

namespace BarkFluff.Client.Core.Services;

internal static class UpdateVersion
{
    public static Version? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var components = value.Trim().Split('.');
        if (components.Length is not (3 or 4))
        {
            return null;
        }

        var numbers = new int[3];
        for (var index = 0; index < numbers.Length; index++)
        {
            if (!int.TryParse(
                    components[index],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var component) ||
                component is < 0 or > ushort.MaxValue)
            {
                return null;
            }

            numbers[index] = component;
        }

        if (components.Length == 4 &&
            (!int.TryParse(
                    components[3],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var revision) ||
             revision is < 0 or > ushort.MaxValue))
        {
            return null;
        }

        return new Version(numbers[0], numbers[1], numbers[2]);
    }

    public static Version? Normalize(Version? value) => value is null
        ? null
        : Normalize($"{value.Major}.{Math.Max(0, value.Minor)}.{Math.Max(0, value.Build)}");

    public static string? Format(Version? value) => value is null
        ? null
        : $"{value.Major}.{value.Minor}.{value.Build}";
}
