using System.Reflection;
using BarkFluff.Shared.Exceptions;
using Barkfluff.Developers.Domain;
using Barkfluff.Developers.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;

namespace Barkfluff.Developers.Infrastructure;

public class ErrorCodeSeeder
{
    public async Task SeedIfNeeded(DevelopersContext context)
    {
        if (await context.ErrorCodes.AnyAsync()) return;

        var exceptionTypes = typeof(BaseGrpcException).Assembly
            .GetTypes()
            .Where(t => t.IsSubclassOf(typeof(BaseGrpcException)) && !t.IsAbstract);

        var entries = new List<ErrorCodeEntry>();

        foreach (var type in exceptionTypes)
        {
            var instance = (BaseGrpcException)Activator.CreateInstance(type)!;

            var ns = type.Namespace ?? "";
            var domain = ns.Split('.').LastOrDefault() ?? "Common";

            entries.Add(new ErrorCodeEntry
            {
                Id = Guid.NewGuid(),
                Code = instance.ErrorCode,
                ExceptionName = type.Name,
                Description = instance.ErrorMessage,
                Domain = domain
            });
        }

        context.ErrorCodes.AddRange(entries);
        await context.SaveChangesAsync();
    }
}
