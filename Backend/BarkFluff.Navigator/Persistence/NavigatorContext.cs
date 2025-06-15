namespace BarkFluff.Navigator.Persistence;

using Domain;
using Microsoft.EntityFrameworkCore;

public class NavigatorContext : DbContext
{
    public NavigatorContext(DbContextOptions<NavigatorContext> options) : base(options) { }
    
    public DbSet<ServerInfo> Servers { get; set; }
}