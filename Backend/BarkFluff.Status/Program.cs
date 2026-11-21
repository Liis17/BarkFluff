using BarkFluff.Status.Api;
using BarkFluff.Status.Configuration;
using BarkFluff.Status.Monitoring;
using BarkFluff.Status.Probing;
using BarkFluff.Status.Reporting;
using BarkFluff.Status.Storage;

var builder = WebApplication.CreateBuilder(args);

// Конфигурация валидируется при старте: неверный NODE_DOMAIN или пин останавливают сервис сразу.
var options = StatusOptions.From(builder.Configuration);
builder.Services.AddSingleton(options);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(new StatusStore(options.DatabasePath));
builder.Services.AddSingleton<ReadinessProbe>();
builder.Services.AddSingleton<StatusReportHolder>();
builder.Services.AddHostedService<StatusMonitorService>();

var app = builder.Build();

app.Use(async (context, next) =>
{
    context.Response.Headers["Content-Security-Policy"] =
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; base-uri 'none'; frame-ancestors 'none'";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    await next(context);
});

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapStatusApi();

app.Run();
