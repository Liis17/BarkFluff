using BarkFluff.Configuration.Domain;

using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Configuration.Infrastructure;

public class ConfigurationContext : DbContext
{
    public ConfigurationContext(DbContextOptions<ConfigurationContext> options) : base(options) { }

    public DbSet<ConfigurationItem> Configurations { get; set; }
}