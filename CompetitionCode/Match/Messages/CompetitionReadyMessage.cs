using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;

namespace Competition.CompetitionCode.Match.Messages;

public struct CompetitionReadyMessage : INetMessage
{
    public ulong PlayerId;
    public bool IsReady;

    public readonly bool ShouldBroadcast => true;
    public readonly NetTransferMode Mode => NetTransferMode.Reliable;
    public readonly LogLevel LogLevel => LogLevel.VeryDebug;
    public readonly bool ShouldBuffer => false;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteULong(PlayerId);
        writer.WriteBool(IsReady);
    }

    public void Deserialize(PacketReader reader)
    {
        PlayerId = reader.ReadULong();
        IsReady = reader.ReadBool();
    }
}
