using BarkFluff.Users.Domain;

using Google.Protobuf.WellKnownTypes;

using ProtoBadge = BarkFluff.Proto.Users.Badge;
using ProtoUserBadge = BarkFluff.Proto.Users.UserBadge;

namespace BarkFluff.Users.Mapping;

public static class BadgeMapping
{
    public static ProtoBadge ToGrpc(this Badge badge)
    {
        return new ProtoBadge
        {
            Id = badge.Id,
            Name = badge.Name,
            Description = badge.Description ?? string.Empty,
            ImageUrl = badge.ImageUrl,
            MiniImageUrl = badge.MiniImageUrl ?? string.Empty,
            CreatedDate = badge.CreatedDate.ToTimestamp(),
            IsActive = badge.IsActive
        };
    }

    public static ProtoUserBadge ToGrpc(this UserBadge userBadge)
    {
        return new ProtoUserBadge
        {
            Badge = userBadge.Badge.ToGrpc(),
            Priority = userBadge.Priority,
            AssignedDate = userBadge.AssignedDate.ToTimestamp(),
            IsPrimary = userBadge.IsPrimary
        };
    }

    public static Badge ToEntity(this ProtoBadge protoBadge)
    {
        return new Badge
        {
            Id = protoBadge.Id,
            Name = protoBadge.Name,
            Description = protoBadge.Description,
            ImageUrl = protoBadge.ImageUrl,
            MiniImageUrl = string.IsNullOrWhiteSpace(protoBadge.MiniImageUrl) ? null : protoBadge.MiniImageUrl,
            CreatedDate = protoBadge.CreatedDate.ToDateTime(),
            IsActive = protoBadge.IsActive
        };
    }
}
