using BarkFluff.Identity.Domain;
using BarkFluff.Identity.Persistence.Contexts;

using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Identity.Persistence.Services;

public class ResetPasswordsStorage (IdentityContext context)
{
    public async Task<ResetPassword?> GetResetPassword(Guid resetId)
    {
        return await context.ResetPasswords.FirstOrDefaultAsync(x => x.Id == resetId);
    }

    public async Task<ResetPassword> AddResetPassword(ResetPassword resetPassword)
    {
        context.ResetPasswords.Add(resetPassword);

        await context.SaveChangesAsync();
        
        return resetPassword;
    }
}