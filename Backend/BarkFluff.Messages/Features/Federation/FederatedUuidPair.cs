using BarkFluff.Shared.Exceptions.Messages;

namespace BarkFluff.Messages.Features.Federation;

// Каноническое сравнение Guid для нормализации UUID-пары fed-DM (docs/rearch/05, README фазы 2):
// string-form ("D") lowercase, ordinal. Реализуемо одинаково на обеих нодах и сторонних реализациях.
public static class FederatedUuidPair
{
    public static (Guid Low, Guid High) Normalize(Guid a, Guid b)
    {
        var sa = a.ToString("D").ToLowerInvariant();
        var sb = b.ToString("D").ToLowerInvariant();
        return string.CompareOrdinal(sa, sb) <= 0 ? (a, b) : (b, a);
    }
}
