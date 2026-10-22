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

    public static SignedKyberPreKey ToGrpc(this OneTimeKyberPrekey prekey)
    {
        return new SignedKyberPreKey
        {
            PrekeyId = (uint)prekey.PrekeyId,
            PublicKey = ByteString.CopyFrom(prekey.PublicKey),
            Signature = ByteString.CopyFrom(prekey.Signature),
        };
    }

    public static SignedKyberPreKey ToLastResortKyberGrpc(this DevicePrekeyBundle bundle)
    {
        return new SignedKyberPreKey
        {
            PrekeyId = (uint)bundle.LastResortKyberPrekeyId!.Value,
            PublicKey = ByteString.CopyFrom(bundle.LastResortKyberPrekeyPublic!),
            Signature = ByteString.CopyFrom(bundle.LastResortKyberPrekeySignature!),
        };
    }

    public static PrekeyBundle ToGrpc(
        this DevicePrekeyBundle bundle,
        OneTimePrekey? oneTimePrekey,
        OneTimeKyberPrekey? kyberPrekey = null,
        bool kyberPrekeyIsLastResort = false)
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

        if (kyberPrekey != null)
        {
            result.KyberPrekey = kyberPrekey.ToGrpc();
            result.KyberPrekeyIsLastResort = kyberPrekeyIsLastResort;
        }

        return result;
    }

    public static PeerDeviceInfo ToPeerInfoGrpc(this UserDevice device, bool hasBundle, bool supportsPqxdh)
    {
        return new PeerDeviceInfo
        {
            DeviceId = device.Id.ToString(),
            DisplayName = string.IsNullOrEmpty(device.CustomName) ? device.OriginalName : device.CustomName,
            HasBundle = hasBundle,
            SupportsPqxdh = supportsPqxdh,
            LastSeenAt = Timestamp.FromDateTime(DateTime.SpecifyKind(device.AuthorizedAt, DateTimeKind.Utc)),
        };
    }
}
