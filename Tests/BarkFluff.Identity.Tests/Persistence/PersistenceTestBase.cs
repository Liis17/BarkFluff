using BarkFluff.Identity.Domain;
using BarkFluff.Identity.Persistence.Contexts;
using BarkFluff.Identity.Persistence.Exceptions;
using BarkFluff.Identity.Persistence.Services;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace BarkFluff.Identity.Tests.Persistence;

public abstract class PersistenceTestBase
{
    protected IdentityContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<IdentityContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new IdentityContext(options);
    }
}
