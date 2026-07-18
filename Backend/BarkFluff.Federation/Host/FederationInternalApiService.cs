using BarkFluff.Proto.FederationInternal;
using BarkFluff.Shared.Identity;

using Microsoft.AspNetCore.Authorization;

namespace BarkFluff.Federation.Host;

// Внутренний API Federation-сервиса. Авторизация — XAuth, TokenType.Service.
// В 1.1 ни один метод не переопределён — весь API отвечает Unimplemented.
[Authorize(Policy = nameof(TokenType.Service))]
public class FederationInternalApiService : FederationInternalApi.FederationInternalApiBase
{
}
