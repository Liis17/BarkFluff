using BarkFluff.Identity.Domain;
using BarkFluff.Identity.Persistence.Contexts;
using BarkFluff.Identity.Persistence.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Identity.Persistence.Services;

public class AuthPropertiesStorage
{
    private readonly IdentityContext _context;

    public AuthPropertiesStorage(IdentityContext context)
    {
        _context = context;
    }

    public async Task<bool> CheckOtpEnabled(long userId)
    {
        var props = await _context.AuthUserProperties.FirstOrDefaultAsync(x => x.UserId == userId);

        return props is not null && props.OtpEnabled;
    }

    public async Task AddUserOtpSecretKey(long userId, string secretKey)
    {
        var props = await _context.AuthUserProperties.FirstOrDefaultAsync(x => x.UserId == userId);

        if (props is null)
        {
            props = new AuthUserProperty()
            {
                OtpEnabled = false,
                UserId = userId,
                OtpSecret = secretKey
            };
            
            await _context.AuthUserProperties.AddAsync(props);
            await _context.SaveChangesAsync();

            return;
        }

        props.OtpSecret = secretKey;
        
        await _context.SaveChangesAsync();
    }
    
    public async Task<string?> GetOtpSecretKey(long userId)
    {
        var props = await _context.AuthUserProperties.FirstOrDefaultAsync(x => x.UserId == userId);
        
        if (props is null)
        {
            throw new OtpNotCreatedException();
        }
        
        return props.OtpSecret;
    }
    
    public async Task EnableOtp(long userId)
    {
        var props = await _context.AuthUserProperties.FirstOrDefaultAsync(x => x.UserId == userId);

        if (props is null)
        {
            throw new OtpNotCreatedException();
        }
        
        props.OtpEnabled = true;
        
        await _context.SaveChangesAsync();
    }

    public async Task<AuthUserProperty?> GetUserAuthProperties(long userId)
    {
        return await _context.AuthUserProperties.Where(x => x.UserId == userId).FirstOrDefaultAsync();
    }

    public async Task DisableOtp(long userId)
    {
        var props = await _context.AuthUserProperties.FirstOrDefaultAsync(x => x.UserId == userId);

        if (props is null)
        {
            throw new OtpNotCreatedException();
        }
        
        props.OtpEnabled = false;
        
        await _context.SaveChangesAsync();
    }

    public async Task DisableEmailOtp(long userId)
    {
        var props = await _context.AuthUserProperties.FirstOrDefaultAsync(x => x.UserId == userId);

        if (props is null)
        {
            throw new OtpNotCreatedException();
        }
        
        props.EmailOtpEnabled = false;
        
        await _context.SaveChangesAsync();
    }

    public async Task UpdateLastEmailAuthCode(long userId, string code)
    {
        var props = await _context.AuthUserProperties.FirstOrDefaultAsync(x => x.UserId == userId);

        if (props is null)
        {
            props = new AuthUserProperty()
            {
                UserId = userId,
                LastEmailAuthCode = code
            };
            
            await _context.AuthUserProperties.AddAsync(props);
            await _context.SaveChangesAsync();

            return;
        }
        
        props.LastEmailAuthCode = code;
        
        await _context.SaveChangesAsync();
    }
}