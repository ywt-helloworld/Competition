using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;

namespace Competition.CompetitionCode.Match.Messages;

public struct CompetitionStartMatchMessage : INetMessage
{
    public string MatchId;
    public string Seed;
    public int AscensionLevel;
    public string SharedCharacterId;
    public ulong HostPlayerId;
    public ulong GuestPlayerId;
    public string ActIds;
    public string ModifierIds;
    public int GameMode;

    public readonly bool ShouldBroadcast => false;
    public readonly NetTransferMode Mode => NetTransferMode.Reliable;
    public readonly LogLevel LogLevel => LogLevel.Info;
    public readonly bool ShouldBuffer => false;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteString(MatchId);
        writer.WriteString(Seed);
        writer.WriteInt(AscensionLevel, 32);
        writer.WriteString(SharedCharacterId);
        writer.WriteULong(HostPlayerId);
        writer.WriteULong(GuestPlayerId);
        writer.WriteString(ActIds);
        writer.WriteString(ModifierIds);
        writer.WriteInt(GameMode, 32);
    }

    public void Deserialize(PacketReader reader)
    {
        MatchId = reader.ReadString();
        Seed = reader.ReadString();
        AscensionLevel = reader.ReadInt(32);
        SharedCharacterId = reader.ReadString();
        HostPlayerId = reader.ReadULong();
        GuestPlayerId = reader.ReadULong();
        ActIds = reader.ReadString();
        ModifierIds = reader.ReadString();
        GameMode = reader.ReadInt(32);
    }
}
