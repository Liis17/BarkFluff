using BarkFluff.Proto.Users;
using BarkFluff.Users.Domain;

using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace BarkFluff.Users.Mapping;

public static class PrekeyMapping
{
    public static SignedPreKey ToGrpc(this DevicePrekeyBundle bundle)
    {
        return new SignedPreKey
        {
            PrekeyId = (uint)bundle.SignedPrekeyId,
            PublicKey = ByteString.CopyFrom(bundle.SignedPrekeyPublic),
            Signature = ByteString.CopyFrom(bundle.SignedPrekeySignature),
        };
    }

    public static OneTimePreKey ToGrpc(this OneTimePrekey prekey)
    {
        return new OneTimePreKey
        {
            PrekeyId = (uint)prekey.PrekeyId,
            PublicKey = ByteString.CopyFrom(prekey.PublicKey),
        };
    }

    public static PrekeyBundle ToGrpc(this DevicePrekeyBundle bundle, OneTimePrekey? oneTimePrekey)
    {
        var result = new PrekeyBundle
        {
            DeviceId = bundle.DeviceId.ToString(),
            RegistrationId = (uint)bundle.RegistrationId,
            IdentityPubkey = ByteString.CopyFrom(bundle.IdentityPubkey),
            SignedPrekey = bundle.ToGrpc(),
            HasOneTimePrekey = oneTimePrekey != null,
        };

        if (oneTimePrekey != null)
        {
            result.OneTimePrekey = oneTimePrekey.ToGrpc();
        }

        return result;
    }

    public static PeerDeviceInfo ToPeerInfoGrpc(this UserDevice device, bool hasBundle)
    {
        return new PeerDeviceInfo
        {
            DeviceId = device.Id.ToString(),
            DisplayName = string.IsNullOrEmpty(device.CustomName) ? device.OriginalName : device.CustomName,
            HasBundle = hasBundle,
            LastSeenAt = Timestamp.FromDateTime(DateTime.SpecifyKind(device.AuthorizedAt, DateTimeKind.Utc)),
        };
    }
}
