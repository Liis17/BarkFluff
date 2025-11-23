using BarkFluff.GrpcServer;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Navigator.Host;
using BarkFluff.Navigator.Persistence;

using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.SetRunningAddress(builder.Configuration);

builder.Services.AddBarkFluffGrpc();
builder.Services.AddGrpcReflection();

builder.Services.AddDbContext<NavigatorContext>(c
    => c.UseNpgsql(builder.Configuration["NavigatorDb"]));

builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<Program>());

builder.Services.AddXAuth(builder.Configuration);

builder.Services.AddTransient<ServersStorage>();

builder.Services.AddMemoryCache();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var ctx = scope.ServiceProvider.GetRequiredService<NavigatorContext>();
    ctx.Database.Migrate();
}

app.MapGrpcReflectionService();
app.UseRouting();
app.UseXAuth();

app.MapGrpcService<NavigatorApiService>();

app.Run();