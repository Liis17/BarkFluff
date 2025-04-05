using Microsoft.AspNetCore.Mvc;
using BarkFluff.Server.CLI.Data;
using BarkFluff.Server.CLI.Models;
using Microsoft.EntityFrameworkCore;

namespace BarkFluff.Server.CLI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class UsersController : ControllerBase
    {
        private readonly MessengerDbContext _context;

        public UsersController(MessengerDbContext context)
        {
            _context = context;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterModel model)
        {
            if (_context.Users.Any(u => u.Username == model.Username))
                return BadRequest("Username already exists");

            var user = new User
            {
                Username = model.Username,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(model.Password) // Используем BCrypt для хэширования
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            return Ok(new { user.UserId, user.Username });
        }
    }

    public class RegisterModel
    {
        public string Username { get; set; }
        public string Password { get; set; }
    }
}