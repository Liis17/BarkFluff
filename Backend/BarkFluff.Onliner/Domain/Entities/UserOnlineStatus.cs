using BarkFluff.Onliner.Domain.Enums;

using System.ComponentModel.DataAnnotations;

namespace BarkFluff.Onliner.Domain.Entities;

public record class UserOnlineStatus
{
    [Key]
    public long UserId { get; init; }

    public StatusTypeId Status { get; init; }

    public DateTime LastSeen { get; init; }
}
