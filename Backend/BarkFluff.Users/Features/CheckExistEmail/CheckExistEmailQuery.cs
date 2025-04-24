using BarkFluff.Proto.Users;
using MediatR;

namespace BarkFluff.Users.Features.CheckExistEmail;

public class CheckExistEmailQuery : IRequest<CheckExistResponse>
{
    public string Email { get; set; }
}