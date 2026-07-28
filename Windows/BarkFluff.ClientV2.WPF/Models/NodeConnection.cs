using BarkFluff.WebApi.Core.MessengerData;

namespace BarkFluff.ClientV2.WPF.Models;

public sealed record NodeConnection(NodeProfile Profile, GlobalParam ConnectionParameters);
