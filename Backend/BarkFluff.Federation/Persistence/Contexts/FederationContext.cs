using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Federation.Persistence.Contexts;

public class FederationContext : DbContext
{
    public FederationContext(DbContextOptions<FederationContext> options)
        : base(options)
    {
    }
}
