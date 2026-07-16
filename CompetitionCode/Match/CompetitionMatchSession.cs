using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Multiplayer.Game;

namespace Competition.CompetitionCode.Match;

/// <summary>State for one Competition lobby and, eventually, its one match.</summary>
public sealed class CompetitionMatchSession
{
    private readonly Dictionary<ulong, bool> _readyByPlayer = new();

    public CompetitionMatchSession(StartRunLobby lobby)
    {
        Lobby = lobby;
        CompetitionTransportService = lobby.NetService;
        IsHost = lobby.LocalPlayer.slotId == 0;
        ResetReadyStates();
    }

    public StartRunLobby Lobby { get; }
    /// <summary>Persistent Competition-only Steam transport; never a Run service.</summary>
    public INetGameService CompetitionTransportService { get; }
    public bool IsHost { get; }
    public string? MatchId { get; private set; }
    public string? Seed { get; private set; }
    public int AscensionLevel { get; private set; }
    public string? SharedCharacterId { get; private set; }
    public bool IsStarting { get; private set; }
    public bool IsInRun { get; private set; }
    public bool LocalReady => GetReady(Lobby.NetService.NetId);
    public bool RemoteReady => Lobby.Players.Any(player => player.id != Lobby.NetService.NetId && GetReady(player.id));

    public bool GetReady(ulong playerId) => _readyByPlayer.TryGetValue(playerId, out bool ready) && ready;

    public void SetReady(ulong playerId, bool ready) => _readyByPlayer[playerId] = ready;

    public void ResetReadyStates()
    {
        _readyByPlayer.Clear();
        foreach (var player in Lobby.Players)
        {
            _readyByPlayer[player.id] = false;
        }
    }

    public void Begin(string matchId, string seed, int ascensionLevel, string sharedCharacterId)
    {
        MatchId = matchId;
        Seed = seed;
        AscensionLevel = ascensionLevel;
        SharedCharacterId = sharedCharacterId;
        IsStarting = true;
    }

    public void MarkInRun()
    {
        IsStarting = false;
        IsInRun = true;
    }

    public void ResetAfterFailedStart()
    {
        MatchId = null;
        Seed = null;
        SharedCharacterId = null;
        AscensionLevel = 0;
        IsStarting = false;
        IsInRun = false;
        ResetReadyStates();
    }
}
