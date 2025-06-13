using BarkFluff.Shared.Exceptions.Identity;
using BarkFluff.Users.Domain;
using BarkFluff.Users.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Users.Persistence.Services;

public class UsersStorage
{
    private readonly UsersContext _usersContext;

    public UsersStorage(UsersContext usersContext)
    {
        _usersContext = usersContext;
    }

    public async Task<User?> GetUserByUsername(string username)
    {
        var user = await _usersContext.Users.Include(u => u.Contact)
            .FirstOrDefaultAsync(x => string.Equals(x.Username.ToLower(), username.ToLower()));
        
        return user;
    }
    
    public async Task<User?> GetUserByEmail(string email)
    {
        var userContact = await _usersContext.UserContacts.Include(u => u.User)
            .FirstOrDefaultAsync(x => string.Equals(x.Email.ToLower(), email.ToLower()));
        
        return userContact?.User;
    }

    public async Task<User?> GetById(long id)
    {
        var user = await _usersContext.Users.Include(u => u.Contact).FirstOrDefaultAsync(x => x.Id == id);

        return user;
    }

    public async Task<List<User>> GetByIds(List<long> ids)
    {
        var users = await _usersContext.Users
            .Include(u => u.Contact)
            .Where(x => ids.Contains(x.Id))
            .ToListAsync();

        return users;
    }

    public async Task<User> CreateUser(string username, string firstName, string lastName, string email)
    {
        var contactUser = new UserContact { Email = email };
        
        var user = new User
        {
            Username = username,
            FirstName = firstName, 
            LastName = lastName,
            RegistrationDate = DateTime.UtcNow,
            Contact = contactUser,
            IsDraft = true,
        };
        
        await _usersContext.Users.AddAsync(user);

        await _usersContext.SaveChangesAsync();

        return user;
    }

    public async Task ChangeDraftStatus(long userId, bool isDraft)
    {
        var user = await _usersContext.Users.FirstOrDefaultAsync(x => x.Id == userId);

        if (user is null)
        {
            throw new UserNotFoundException();
        }
        
        user.IsDraft = isDraft;
        
        await _usersContext.SaveChangesAsync();
    }

     public async Task UpdateProfilePicture(long userId, string profilePictureUrl)
     {
        var user = await _usersContext.Users.FirstOrDefaultAsync(x => x.Id == userId);
        
        if (user is null)
        {
            throw new UserNotFoundException();
        }
        
        user.ProfilePicture = profilePictureUrl;
        await _usersContext.SaveChangesAsync();
     }
    

    public async Task UpdateTrackedUser(User user)
    {
        _usersContext.Users.Update(user);

        await _usersContext.SaveChangesAsync();
    }

    public async Task ChangeName(long userId, string firstName, string lastName)
    {
        var user = await _usersContext.Users.FirstOrDefaultAsync(x => x.Id == userId);

        if (user is null)
        {
            throw new UserNotFoundException();
        }
        
        user.FirstName = firstName;
        user.LastName = lastName;
        
        await _usersContext.SaveChangesAsync();
    }

    public async Task ChangeUsername(long userId, string username)
    {
        var user = await _usersContext.Users.FirstOrDefaultAsync(x => x.Id == userId);
        if (user is null)
        {
            throw new UserNotFoundException();
        }
        
        user.Username = username;
        
        await _usersContext.SaveChangesAsync();
    }
    
    public async Task ChangeBio(long userId, string newBio)
    {
        var user = await _usersContext.Users.FirstOrDefaultAsync(x => x.Id == userId);

        if (user is null)
        {
            throw new UserNotFoundException();
        }
        
        user.Bio = newBio;
        
        await _usersContext.SaveChangesAsync();
    }
}