using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace BarkFluff.Node;

public static class NodeSubdomains
{
    private static readonly IReadOnlyDictionary<string, string> Subdomains = new ReadOnlyDictionary<string, string>(
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["identity"] = "identity",
            ["users"] = "users",
            ["messages"] = "messages",
            ["files"] = "files",
            ["files-media"] = "files2",
            ["updates"] = "updates",
            ["onliner"] = "onliner",
            ["fast-auth"] = "fast-auth",
            ["federation"] = "federation",
            ["beacon"] = "beacon",
            ["bots"] = "bots",
            ["calls"] = "calls",
            ["livekit"] = "livekit",
            ["web"] = "web",
            ["developers"] = "developers",
            ["admin-panel"] = "panel",
            ["setup"] = "setup"
        });

    public static IReadOnlyDictionary<string, string> All => Subdomains;

    public static string Get(string serviceOrSiteKey) => Subdomains[serviceOrSiteKey];

    public static string BuildUrl(string serviceOrSiteKey, string domain, string scheme = "https") =>
        $"{scheme}://{Get(serviceOrSiteKey)}.{domain}";
}
