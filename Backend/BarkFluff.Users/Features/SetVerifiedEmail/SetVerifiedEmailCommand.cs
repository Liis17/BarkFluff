using BarkFluff.Proto.Users;
using BarkFluff.Users.Domain;
using BarkFluff.Users.Persistence.Services;
using Grpc.Core;
using MediatR;

namespace BarkFluff.Users.Features.SetVerifiedEmail;

public record SetVerifiedEmailCommand(long UserId, string Email) : IRequest<SetVerifiedEmailResponse>;

public class SetVerifiedEmailCommandHandler(UsersStorage users) : IRequestHandler<SetVerifiedEmailCommand, SetVerifiedEmailResponse>
{
    public async Task<SetVerifiedEmailResponse> Handle(SetVerifiedEmailCommand request, CancellationToken ct)
    {
        var email = request.Email.Trim();
        if (!System.Net.Mail.MailAddress.TryCreate(email, out var address) || address.Address != email)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid email"));
        var user = await users.GetById(request.UserId);
        if (user == null || user.IsBot || user.IsDraft)
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "Account is not active"));
        var owner = await users.GetUserByEmail(email);
        if (owner != null && owner.Id != user.Id)
            throw new BarkFluff.Shared.Exceptions.Identity.EmailExistException();
        user.Contact ??= new Domain.UserContact { UserId = user.Id };
        user.Contact.Email = email;
        await users.UpdateTrackedUser(user);
        return new SetVerifiedEmailResponse();
    }
}
