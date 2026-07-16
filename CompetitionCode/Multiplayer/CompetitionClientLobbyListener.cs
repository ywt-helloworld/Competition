using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;

namespace Competition.CompetitionCode.Multiplayer;

/// <summary>
/// Receives the original StartRunLobby events for a Competition client.
/// It never starts a run.
/// </summary>
public sealed class CompetitionClientLobbyListener : IStartRunLobbyListener
{
    private readonly CompetitionClientController _controller;

    public CompetitionClientLobbyListener(CompetitionClientController controller)
    {
        _controller = controller;
    }

    public void PlayerConnected(LobbyPlayer player)
    {
        MainFile.Logger.Info($"Client lobby player connected: {player.id}.");
        _controller.NotifyLobbyStateChanged();
        if (_controller.CurrentLobby is { } lobby)
        {
            Competition.CompetitionCode.Match.CompetitionMatchController.ResetReadyForLobbyChange(lobby, "player connected");
        }
    }

    public void PlayerChanged(LobbyPlayer player, bool isRandomCharacterResolution)
    {
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
        MainFile.Logger.Info($"Remote player disconnected: {player.id}.");
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
        _controller.OnHostDisconnected(info);
    }
}
