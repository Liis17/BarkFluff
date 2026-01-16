using BarkFluff.Identity.Domain;
using BarkFluff.Identity.Persistence.Contexts;

using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Identity.Persistence.Services;

public class PasswordsStorage
{
    private readonly IdentityContext _context;

    public PasswordsStorage(IdentityContext context)
    {
        _context = context;
    }

    public async Task<bool> UpdateUserPasswordHash(long userId, string passwordHash)
    {
        var userPassword = await _context.UserPasswords.FirstOrDefaultAsync(x => x.UserId == userId);

        if (userPassword is null)
        {
            userPassword = new UserPassword { UserId = userId, ChangedAt = DateTime.UtcNow, PasswordHash = passwordHash };
            
            _context.UserPasswords.Add(userPassword);
            
            await _context.SaveChangesAsync();

            return true;
        }

        userPassword.ChangedAt = DateTime.UtcNow;
        userPassword.PasswordHash = passwordHash;
        
        _context.UserPasswords.Update(userPassword);
        
        await _context.SaveChangesAsync();

        return false;
    }

    public async Task<string?> GetUserPasswordHash(long userId)
    {
        var userPassword = await _context.UserPasswords
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == userId);
        
        return userPassword?.PasswordHash;
    }
}