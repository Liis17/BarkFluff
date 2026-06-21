using BarkFluff.Calls.Domain;
using BarkFluff.Calls.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Calls.Persistence;

public class CallsContext : DbContext
{
    public CallsContext(DbContextOptions<CallsContext> options) : base(options) { }

    public DbSet<CallSession> CallSessions { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new CallSessionConfiguration());

        base.OnModelCreating(modelBuilder);
    }
}
