using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;

namespace Competition.CompetitionCode.Match.Messages;

public struct CompetitionStartAckMessage : INetMessage
{
    public string MatchId;
    public ulong PlayerId;
    public bool Success;
    public string Error;

    public readonly bool ShouldBroadcast => false;
    public readonly NetTransferMode Mode => NetTransferMode.Reliable;
    public readonly LogLevel LogLevel => LogLevel.Info;
    public readonly bool ShouldBuffer => false;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteString(MatchId);
        writer.WriteULong(PlayerId);
        writer.WriteBool(Success);
        writer.WriteString(Error);
    }

    public void Deserialize(PacketReader reader)
    {
        MatchId = reader.ReadString();
        PlayerId = reader.ReadULong();
        Success = reader.ReadBool();
        Error = reader.ReadString();
    }
}
