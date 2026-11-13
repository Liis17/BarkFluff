using System.Security.Cryptography;
using BarkFluff.Bft;

namespace BarkFluff.BftProxy;

internal static class BftProxyCli
{
    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        if (args.Length == 0) return await RunServerAsync(cancellationToken).ConfigureAwait(false);
        return args[0] switch
        {
            "genkey" when args.Length == 1 => GenerateKeyPair(),
            "add-client" => await AddClientAsync(args[1..], cancellationToken).ConfigureAwait(false),
            "remove-client" => await RemoveClientAsync(args[1..], cancellationToken).ConfigureAwait(false),
            _ => throw new InvalidOperationException("Usage: BarkFluff.BftProxy [genkey | add-client --label <label> --name <name> --host <host> --port <port> [--token <token>] | remove-client --label <label>].")
        };
    }

    private static int GenerateKeyPair()
    {
        var privateKey = RandomNumberGenerator.GetBytes(32);
        try
        {
            var publicKey = BftCrypto.GetX25519PublicKey(privateKey);
            Console.WriteLine($"BFT_STATIC_PRIVATE_KEY={EncodeKey(privateKey)}");
            Console.WriteLine($"public={EncodeKey(publicKey)}");
            CryptographicOperations.ZeroMemory(publicKey);
            return 0;
        }
        finally { CryptographicOperations.ZeroMemory(privateKey); }
    }

    private static async Task<int> AddClientAsync(string[] args, CancellationToken cancellationToken)
    {
        var options = ParseOptions(args, "--label", "--name", "--host", "--port", "--token");
        var label = options["--label"];
        if (!System.Text.RegularExpressions.Regex.IsMatch(label, "^[A-Za-z0-9._-]{1,64}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant))
            throw new InvalidOperationException("--label must contain 1–64 ASCII letters, digits, dots, underscores, or hyphens.");
        if (!int.TryParse(options["--port"], out var port) || port is < 1 or > 65535)
            throw new InvalidOperationException("--port must be between 1 and 65535.");

        var keyFile = RequireEnvironment("BFT_CLIENT_KEYS_FILE");
        var staticKey = ProxyConfiguration.DecodeBase64UrlKey(RequireEnvironment("BFT_STATIC_PRIVATE_KEY"), "BFT_STATIC_PRIVATE_KEY");
        var path = Path.GetFullPath(keyFile);
        var prior = File.Exists(path) ? await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false) : [];
        IReadOnlyList<BftServerObfsKey> entries;
        try { entries = ClientKeyFile.Parse(prior); }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or System.Text.DecoderFallbackException)
        {
            throw new InvalidOperationException("BFT_CLIENT_KEYS_FILE is invalid; no key was added.");
        }
        if (entries.Any(entry => entry.Label == label)) throw new InvalidOperationException("A client with this label already exists.");
        if (entries.Count >= 1024) throw new InvalidOperationException("BFT_CLIENT_KEYS_FILE already contains 1024 clients.");

        var obfsKey = RandomNumberGenerator.GetBytes(32);
        try
        {
            var link = BftConnectionString.Format(new BftConnectionSettings(
                BftCrypto.GetX25519PublicKey(staticKey), obfsKey, options["--host"], port,
                options.GetValueOrDefault("--token")) { Name = options["--name"] });
            var line = $"{label} {EncodeKey(obfsKey)} # {options["--name"]}";
            var prefix = prior.Length > 0 && prior[^1] != (byte)'\n' ? "\n" : string.Empty;
            await File.AppendAllTextAsync(path, prefix + line + Environment.NewLine, new System.Text.UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
            Console.WriteLine(link);
            return 0;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(staticKey);
            CryptographicOperations.ZeroMemory(prior);
            CryptographicOperations.ZeroMemory(obfsKey);
        }
    }

    private static async Task<int> RemoveClientAsync(string[] args, CancellationToken cancellationToken)
    {
        var options = ParseOptions(args, "--label");
        var label = options["--label"];
        var path = Path.GetFullPath(RequireEnvironment("BFT_CLIENT_KEYS_FILE"));
        var lines = await File.ReadAllLinesAsync(path, cancellationToken).ConfigureAwait(false);
        var filtered = lines.Where(line =>
        {
            var content = line.Split('#', 2)[0].Trim();
            var separator = content.IndexOfAny([' ', '\t']);
            var candidate = separator < 0 ? content : content[..separator];
            return !string.Equals(candidate, label, StringComparison.Ordinal);
        }).ToArray();
        if (filtered.Length == lines.Length) throw new InvalidOperationException("No client with this label exists.");
        await File.WriteAllLinesAsync(path, filtered, new System.Text.UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
        Console.WriteLine($"Removed client {label}.");
        return 0;
    }

    private static Dictionary<string, string> ParseOptions(string[] args, params string[] allowed)
    {
        var required = allowed.Where(key => key != "--token").ToHashSet(StringComparer.Ordinal);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i += 2)
        {
            if (i + 1 >= args.Length || !allowed.Contains(args[i], StringComparer.Ordinal) || !values.TryAdd(args[i], args[i + 1]))
                throw new InvalidOperationException("Invalid or repeated CLI option.");
        }
        if (required.Any(key => !values.ContainsKey(key))) throw new InvalidOperationException("A required CLI option is missing.");
        return values;
    }

    private static async Task<int> RunServerAsync(CancellationToken cancellationToken)
    {
        var configuration = ProxyConfiguration.FromEnvironment();
        await using IAsyncDisposable host = configuration.Mode == ProxyMode.Relay
            ? await BftRelayHost.StartAsync(configuration.Relay!, cancellationToken: cancellationToken).ConfigureAwait(false)
            : await BftProxyHost.StartExitAsync(configuration.Exit!, cancellationToken: cancellationToken).ConfigureAwait(false);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Console.CancelKeyPress += HandleCancel;
        try { await Task.Delay(Timeout.InfiniteTimeSpan, stop.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        finally { Console.CancelKeyPress -= HandleCancel; }
        return 0;

        void HandleCancel(object? sender, ConsoleCancelEventArgs eventArgs)
        {
            eventArgs.Cancel = true;
            stop.Cancel();
        }
    }

    private static string RequireEnvironment(string name) =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name))
            ? Environment.GetEnvironmentVariable(name)!.Trim()
            : throw new InvalidOperationException($"{name} is required for this command.");

    private static string EncodeKey(ReadOnlySpan<byte> value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
