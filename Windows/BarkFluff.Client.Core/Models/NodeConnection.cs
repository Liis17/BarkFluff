using BarkFluff.WebApi.Core.MessengerData;

namespace BarkFluff.Client.Core.Models;

public sealed record NodeConnection(NodeProfile Profile, GlobalParam ConnectionParameters);
