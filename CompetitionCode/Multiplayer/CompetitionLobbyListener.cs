using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;

namespace Competition.CompetitionCode.Multiplayer;

/// <summary>
/// Receives state changes from the real StartRunLobby. Run startup intentionally
/// remains out of scope until the Competition run flow exists.
/// </summary>
public sealed class CompetitionLobbyListener : IStartRunLobbyListener
{
    private readonly CompetitionHostController _controller;

    public CompetitionLobbyListener(CompetitionHostController controller)
    {
        _controller = controller;
    }

    public void PlayerConnected(LobbyPlayer player)
    {
        if (_controller.CurrentLobby?.NetService.NetId == player.id)
        {
            MainFile.Logger.Info($"Lobby player connected: {player.id}.");
        }
        else
        {
            MainFile.Logger.Info($"Host transport received incoming connection: player={player.id}.");
            MainFile.Logger.Info($"Remote player connected: {player.id}.");
            MainFile.Logger.Info($"Host lobby player count: {_controller.CurrentLobby?.Players.Count ?? 0}.");
        }

        _controller.NotifyLobbyStateChanged();
        if (_controller.CurrentLobby is { } lobby)
        {
            Competition.CompetitionCode.Match.CompetitionMatchController.ResetReadyForLobbyChange(lobby, "player connected");
        }
    }

    public void PlayerChanged(LobbyPlayer player, bool isRandomCharacterResolution)
    {
        MainFile.Logger.Info($"Lobby player changed: {player.id}.");
        _controller.NotifyLobbyStateChanged();
        if (_controller.CurrentLobby is { } lobby)
        {
            Competition.CompetitionCode.Match.CompetitionMatchController.ResetReadyForLobbyChange(lobby, "player disconnected");
        }
    }

    public void AscensionChanged()
    {
        _controller.NotifyLobbyStateChanged();
    }

    public void SeedChanged()
    {
    }

    public void ModifiersChanged()
    {
    }

    public void MaxAscensionChanged()
    {
        _controller.NotifyLobbyStateChanged();
    }

    public void RemotePlayerDisconnected(LobbyPlayer player)
    {
        // StartRunLobby's listener callback does not expose NetErrorInfo. In
        // the supported client-back flow this is the locally initiated Quit.
        MainFile.Logger.Info($"Remote player disconnected: Quit ({player.id}).");
        MainFile.Logger.Info($"Host lobby player count: {_controller.CurrentLobby?.Players.Count ?? 0}.");
        if (_controller.CurrentLobby is { } lobby)
        {
            Competition.CompetitionCode.Match.CompetitionMatchController.ResetReadyForLobbyChange(lobby, "player disconnected");
        }
        _controller.NotifyLobbyStateChanged();
    }

    public void BeginRun(string seed, List<ActModel> acts, IReadOnlyList<ModifierModel> modifiers)
    {
        MainFile.Logger.Info("BeginRun requested, but run startup is not implemented.");
    }

    public void LocalPlayerDisconnected(NetErrorInfo info)
    {
        MainFile.Logger.Warn($"Competition lobby disconnected: {info.GetReason()}.");
        _controller.NotifyLobbyStateChanged();
    }
}
