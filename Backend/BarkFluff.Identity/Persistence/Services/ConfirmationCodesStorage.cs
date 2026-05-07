using BarkFluff.Identity.Domain;
using BarkFluff.Identity.Persistence.Contexts;

using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Identity.Persistence.Services;

public class ConfirmationCodesStorage
{
    private readonly IdentityContext _context;

    public ConfirmationCodesStorage(IdentityContext context)
    {
        _context = context;
    }

    public async Task<ConfirmationCode> AddCode(ConfirmationCode confirmationCode)
    {
        _context.ConfirmationCodes.Add(confirmationCode);

        await _context.SaveChangesAsync();

        return confirmationCode;
    }

    public async Task<ConfirmationCode?> GetCode(Guid id)
    {
        return await _context.ConfirmationCodes.FirstOrDefaultAsync(x => x.Id == id);
    }

    public async Task DeleteCode(Guid id)
    {
        var code = await _context.ConfirmationCodes.FirstOrDefaultAsync(x => x.Id == id);
        if (code is null)
            return;

        _context.ConfirmationCodes.Remove(code);
        await _context.SaveChangesAsync();
    }
}