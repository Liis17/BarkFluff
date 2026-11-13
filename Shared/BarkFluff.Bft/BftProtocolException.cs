namespace BarkFluff.Bft;

public sealed class BftProtocolException : IOException
{
    public BftProtocolException(string message) : base(message) { }

    public BftProtocolException(string message, Exception innerException) : base(message, innerException) { }
}

public interface IBftRandomSource
{
    void Fill(Span<byte> destination);
}

public sealed class BftSystemRandomSource : IBftRandomSource
{
    public static BftSystemRandomSource Shared { get; } = new();

    private BftSystemRandomSource() { }

    public void Fill(Span<byte> destination) => System.Security.Cryptography.RandomNumberGenerator.Fill(destination);
}
