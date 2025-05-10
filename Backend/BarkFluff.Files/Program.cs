using Amazon.S3;
using BarkFluff.Files.Extensions;
using BarkFluff.Files.Host;
using BarkFluff.Files.Infrastructure;
using BarkFluff.Files.Persistence;
using BarkFluff.GrpcServer;
using BarkFluff.GrpcServer.XAuth;
using BarkFluff.Shared.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql.EntityFrameworkCore.PostgreSQL;

namespace BarkFluff.Files;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.LoadConfiguration(ServiceId.Files);
        builder.SetRunningAddress(builder.Configuration);

        // Регистрируем gRPC сервисы с интерцепторами
        builder.Services.AddGrpc(options =>
        {
            options.Interceptors.Add<ServerExceptionInterceptor>();
        });
        
        builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<Program>());

        builder.Services.AddGrpcReflection();
        
        builder.Services.AddXAuth(builder.Configuration);

        builder.Services.AddControllers();
        
        builder.Services.AddScoped<UploadedFilesStorage>();

        builder.Services.AddMinioS3(builder.Configuration);
        
        // Регистрируем S3Uploader
        builder.Services.AddScoped<S3Uploader>();

        builder.Services.AddDbContext<FilesContext>(options =>
            options.UseNpgsql(builder.Configuration["FilesDb"]));
        
        var app = builder.Build();
        
        app.MapGrpcReflectionService();

        app.UseXAuth();
        
        app.MapControllers();
        
        app.MapGrpcService<FilesApiService>();
        app.MapGrpcService<FilesServerApiService>();

        app.Run();
        
    }
}