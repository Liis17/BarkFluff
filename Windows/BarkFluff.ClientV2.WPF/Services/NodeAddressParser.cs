using System.Net;

namespace BarkFluff.ClientV2.WPF.Services;

public sealed class NodeAddressParser : INodeAddressParser
{
    public NodeAddressParseResult Parse(string? address)
    {
        var input = address?.Trim();
        if (string.IsNullOrWhiteSpace(input))
        {
            return NodeAddressParseResult.Failure(NodeAddressError.Required);
        }

        var hasScheme = input.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || input.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        var candidate = hasScheme ? input : $"https://{input}";

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrWhiteSpace(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || (uri.AbsolutePath != "/" && !string.IsNullOrEmpty(uri.AbsolutePath)))
        {
            return NodeAddressParseResult.Failure(NodeAddressError.Invalid);
        }

        if (uri.Port is < 1 or > 65535)
        {
            return NodeAddressParseResult.Failure(NodeAddressError.Invalid);
        }

        if (IPAddress.TryParse(uri.Host, out _) && !HasExplicitPort(input))
        {
            return NodeAddressParseResult.Failure(NodeAddressError.IpPortRequired);
        }

        return NodeAddressParseResult.Success(uri);
    }

    private static bool HasExplicitPort(string input)
    {
        var authority = input;
        var schemeSeparator = authority.IndexOf("://", StringComparison.Ordinal);
        if (schemeSeparator >= 0)
        {
            authority = authority[(schemeSeparator + 3)..];
        }

        var end = authority.IndexOfAny(['/', '?', '#']);
        if (end >= 0)
        {
            authority = authority[..end];
        }

        if (authority.StartsWith('['))
        {
            var bracketEnd = authority.IndexOf(']');
            return bracketEnd >= 0 && authority.Length > bracketEnd + 1 && authority[bracketEnd + 1] == ':';
        }

        return authority.LastIndexOf(':') >= 0;
    }
}
