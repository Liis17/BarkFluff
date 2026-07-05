using BarkFluff.Bots.Domain;
using BarkFluff.Proto.Bots;

using MediatR;

namespace BarkFluff.Bots.Features.CreateSystemBot;

public class CreateSystemBotCommand : IRequest<CreateSystemBotResponse>
{
    public string Username { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Роль системного бота (задаёт только SystemBotsSeeder, из AdminPanel всегда None)</summary>
    public SystemBotRole SystemRole { get; set; } = SystemBotRole.None;

    /// <summary>Пропустить проверку формата/суффикса username (только для сидера, напр. botfather)</summary>
    public bool BypassUsernameRules { get; set; }
}
