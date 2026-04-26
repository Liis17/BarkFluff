using BarkFluff.Proto.Identity;

using MediatR;

namespace BarkFluff.Identity.Features.Logout;

public class LogoutCommand : IRequest<LogoutResponse>;
