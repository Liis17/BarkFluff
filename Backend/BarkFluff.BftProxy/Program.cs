namespace BarkFluff.BftProxy;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try { return await BftProxyCli.RunAsync(args).ConfigureAwait(false); }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"BarkFluff BFT Proxy: {ex.Message}");
            return 1;
        }
    }
}
