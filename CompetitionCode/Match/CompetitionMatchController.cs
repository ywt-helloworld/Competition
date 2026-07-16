using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Managers;
using Competition.CompetitionCode.Match.Messages;
using Competition.CompetitionCode.Multiplayer;
using Competition.CompetitionCode.UI;

namespace Competition.CompetitionCode.Match;

/// <summary>
/// Competition uses StartRunLobby solely as a Steam transport. Readiness and
/// start are this protocol, never StartRunLobby.SetReady, so the final run is
/// always created through NGame.StartNewSingleplayerRun.
/// </summary>
public static class CompetitionMatchController
{
    private enum StartAckState { Pending, Success, Failure }

    private static readonly HashSet<string> _handledMatchIds = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, StartAckState> _localAckStates = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, StartAckState> _remoteAckStates = new(StringComparer.Ordinal);
    private static CompetitionMatchSession? _session;
    // This reference is the Competition transport. It is intentionally kept
    // outside RunManager so custom messages survive an isolated local run.
    private static INetGameService? _competitionTransportService;
    private static OriginalRunSnapshot? _originalRunSnapshot;

    public static CompetitionMatchSession? Current => _session;
    public static event Action? StateChanged;

    public static void AttachLobby(StartRunLobby lobby)
    {
        if (ReferenceEquals(_session?.Lobby, lobby))
        {
            return;
        }

        DetachLobby();
        _session = new CompetitionMatchSession(lobby);
        _competitionTransportService = _session.CompetitionTransportService;
        _competitionTransportService.RegisterMessageHandler<CompetitionReadyMessage>(OnReadyMessage);
        _competitionTransportService.RegisterMessageHandler<CompetitionStartMatchMessage>(OnStartMatchMessage);
        _competitionTransportService.RegisterMessageHandler<CompetitionStartAckMessage>(OnStartAckMessage);
        MainFile.Logger.Info("Character selection deferred until match start.");
        NotifyStateChanged();
    }

    public static void DetachLobby(StartRunLobby? lobby = null)
    {
        if (lobby != null && !ReferenceEquals(_session?.Lobby, lobby))
        {
            return;
        }

        if (_competitionTransportService != null)
        {
            _competitionTransportService.UnregisterMessageHandler<CompetitionReadyMessage>(OnReadyMessage);
            _competitionTransportService.UnregisterMessageHandler<CompetitionStartMatchMessage>(OnStartMatchMessage);
            _competitionTransportService.UnregisterMessageHandler<CompetitionStartAckMessage>(OnStartAckMessage);
        }

        _competitionTransportService = null;
        _session = null;
        _localAckStates.Clear();
        _remoteAckStates.Clear();
        NotifyStateChanged();
    }

    public static bool IsReady(StartRunLobby lobby, ulong playerId) =>
        ReferenceEquals(_session?.Lobby, lobby) && _session.GetReady(playerId);

    /// <summary>Used only by isolation patches to identify this mod's lobby.</summary>
    internal static bool IsCompetitionLobby(StartRunLobby lobby) => ReferenceEquals(_session?.Lobby, lobby);

    public static void EndCompetitionRunForReturnToMenu()
    {
        if (!CompetitionRunContext.IsCompetitionRun)
        {
            return;
        }

        _originalRunSnapshot?.LogUnchanged();
        CompetitionRunContext.Exit("returned to main menu");
        if (_session is { IsHost: true })
        {
            CompetitionHostController.TryGet()?.CloseHostLobby();
        }
        else
        {
            CompetitionClientController.TryGet()?.CloseClientLobby();
        }
    }

    public static void ToggleLocalReady()
    {
        CompetitionMatchSession? session = _session;
        if (session == null || session.IsStarting || session.IsInRun)
        {
            return;
        }

        ulong localId = session.CompetitionTransportService.NetId;
        bool ready = !session.GetReady(localId);
        ApplyReady(session, localId, ready);
        session.CompetitionTransportService.SendMessage(new CompetitionReadyMessage { PlayerId = localId, IsReady = ready });
        MainFile.Logger.Info($"Local ready changed: {ready.ToString().ToLowerInvariant()}.");
        MainFile.Logger.Info("Ready message sent.");
        if (session.IsHost)
        {
            TryBeginAsHost(session);
        }
    }

    public static void ResetReadyForLobbyChange(StartRunLobby lobby, string reason)
    {
        CompetitionMatchSession? session = _session;
        if (session == null || !ReferenceEquals(session.Lobby, lobby) || session.IsStarting || session.IsInRun)
        {
            return;
        }

        session.ResetReadyStates();
        _remoteAckStates.Clear();
        if (session.IsHost)
        {
            foreach (LobbyPlayer player in lobby.Players)
            {
                session.CompetitionTransportService.SendMessage(new CompetitionReadyMessage { PlayerId = player.id, IsReady = false });
            }
        }

        MainFile.Logger.Info($"Competition ready states reset: {reason}.");
        NotifyStateChanged();
    }

    private static void OnReadyMessage(CompetitionReadyMessage message, ulong senderId)
    {
        CompetitionMatchSession? session = _session;
        if (session == null || session.IsStarting || session.IsInRun ||
            !session.Lobby.Players.Exists(player => player.id == message.PlayerId))
        {
            return;
        }

        if (senderId != message.PlayerId && senderId != GetHostPlayerId(session))
        {
            MainFile.Logger.Warn("Ignoring ready message with mismatched sender.");
            return;
        }

        ApplyReady(session, message.PlayerId, message.IsReady);
        if (session.IsHost)
        {
            TryBeginAsHost(session);
        }
    }

    private static void ApplyReady(CompetitionMatchSession session, ulong playerId, bool ready)
    {
        if (session.GetReady(playerId) == ready)
        {
            return;
        }

        session.SetReady(playerId, ready);
        if (playerId != session.CompetitionTransportService.NetId)
        {
            MainFile.Logger.Info($"Remote ready changed: {ready.ToString().ToLowerInvariant()}.");
        }
        NotifyStateChanged();
    }

    private static void TryBeginAsHost(CompetitionMatchSession session)
    {
        if (!session.IsHost || session.IsStarting || session.IsInRun)
        {
            return;
        }
        if (session.Lobby.Players.Count != 2)
        {
            MainFile.Logger.Info("Start unavailable: lobby requires exactly 2 players.");
            return;
        }
        if (session.Lobby.Players.Any(player => !session.GetReady(player.id)))
        {
            return;
        }

        MainFile.Logger.Info("Both players are ready.");
        TaskHelper.RunSafely(BeginHostMatchAsync(session));
    }

    private static async Task BeginHostMatchAsync(CompetitionMatchSession session)
    {
        if (!ReferenceEquals(_session, session) || session.IsStarting || session.IsInRun)
        {
            return;
        }

        try
        {
            MainFile.Logger.Info("Host beginning Competition match.");
            MainFile.Logger.Info("Host selecting shared character.");
            CharacterModel sharedCharacter = Integration.CompetitionCharacterProvider.GetRandomSharedBaseCharacter();
            string matchId = Guid.NewGuid().ToString("N");
            string seed = SeedHelper.GetRandomSeed();
            string characterId = sharedCharacter.Id.ToString();
            IReadOnlyList<ActModel> acts = ActModel.GetDefaultList();
            IReadOnlyList<ModifierModel> modifiers = session.Lobby.Modifiers;
            session.Begin(matchId, seed, session.Lobby.Ascension, characterId);
            _handledMatchIds.Add(matchId);
            NotifyStateChanged();

            MainFile.Logger.Info($"Shared character selected: {characterId}.");
            MainFile.Logger.Info($"Match id: {matchId}.");
            MainFile.Logger.Info($"Match seed: {seed}.");
            MainFile.Logger.Info($"Match ascension: {session.Lobby.Ascension}.");
            MainFile.Logger.Info("Sending CompetitionStartMatchMessage.");
            List<LobbyPlayer> players = session.Lobby.Players.OrderBy(player => player.slotId).ToList();
            session.CompetitionTransportService.SendMessage(new CompetitionStartMatchMessage
            {
                MatchId = matchId, Seed = seed, AscensionLevel = session.Lobby.Ascension, SharedCharacterId = characterId,
                HostPlayerId = players[0].id, GuestPlayerId = players[1].id,
                ActIds = string.Join(",", acts.Select(act => act.Id.ToString())),
                ModifierIds = string.Join(",", modifiers.Select(modifier => modifier.Id.ToString())), GameMode = (int)GameMode.Standard
            });
            await StartCompetitionRunAsync(session, matchId, seed, session.Lobby.Ascension, sharedCharacter, acts, modifiers);
            NotifyPostStart(session);
        }
        catch (Exception exception)
        {
            HandleStartFailure(session, exception);
        }
    }

    private static void OnStartMatchMessage(CompetitionStartMatchMessage message, ulong senderId)
    {
        CompetitionMatchSession? session = _session;
        if (session == null || session.IsHost || senderId != GetHostPlayerId(session))
        {
            return;
        }
        if (_handledMatchIds.Contains(message.MatchId) || session.IsStarting || session.IsInRun)
        {
            MainFile.Logger.Info($"Duplicate start message ignored: {message.MatchId}.");
            return;
        }
        TaskHelper.RunSafely(BeginGuestMatchAsync(session, message));
    }

    private static async Task BeginGuestMatchAsync(CompetitionMatchSession session, CompetitionStartMatchMessage message)
    {
        try
        {
            MainFile.Logger.Info("CompetitionStartMatchMessage received.");
            MainFile.Logger.Info($"Shared character received: {message.SharedCharacterId}.");
            CharacterModel? character = Integration.CompetitionCharacterProvider.ResolveSharedBaseCharacter(message.SharedCharacterId);
            if (character == null) throw new InvalidOperationException("shared character could not be resolved");
            IReadOnlyList<ActModel> acts = ResolveActs(message.ActIds);
            IReadOnlyList<ModifierModel> modifiers = ResolveModifiers(message.ModifierIds);
            if ((GameMode)message.GameMode != GameMode.Standard || string.IsNullOrWhiteSpace(message.Seed) || message.AscensionLevel < 0)
                throw new InvalidOperationException("received invalid Competition match parameters");

            _handledMatchIds.Add(message.MatchId);
            session.Begin(message.MatchId, SeedHelper.CanonicalizeSeed(message.Seed), message.AscensionLevel, message.SharedCharacterId);
            NotifyStateChanged();
            MainFile.Logger.Info($"Shared character resolved: {character.Id}.");
            MainFile.Logger.Info($"Match seed received: {session.Seed}.");
            MainFile.Logger.Info($"Match ascension received: {session.AscensionLevel}.");
            await StartCompetitionRunAsync(session, message.MatchId, session.Seed!, session.AscensionLevel, character, acts, modifiers);
            FinalizeLocalAck(session, message.MatchId, true, string.Empty);
            NotifyPostStart(session);
        }
        catch (Exception exception)
        {
            FinalizeLocalAck(session, message.MatchId, false, exception.Message);
            HandleStartFailure(session, exception);
        }
    }

    private static void OnStartAckMessage(CompetitionStartAckMessage message, ulong senderId)
    {
        if (_session is not { IsHost: true } session || message.MatchId != session.MatchId || senderId != message.PlayerId)
        {
            return;
        }
        string key = $"{message.MatchId}:{message.PlayerId}";
        StartAckState received = message.Success ? StartAckState.Success : StartAckState.Failure;
        if (_remoteAckStates.TryGetValue(key, out StartAckState existing))
        {
            MainFile.Logger.Info(existing == received
                ? $"Duplicate start ACK ignored: {message.MatchId}."
                : $"Conflicting start ACK ignored: {message.MatchId}.");
            return;
        }
        _remoteAckStates[key] = received;
        MainFile.Logger.Info($"Start ACK finalized: {received.ToString().ToLowerInvariant()}, match={message.MatchId}.");
        if (received == StartAckState.Failure)
        {
            MainFile.Logger.Error($"Guest rejected Competition start: {message.Error}.");
        }
    }

    private static void FinalizeLocalAck(CompetitionMatchSession session, string matchId, bool success, string error)
    {
        if (string.IsNullOrWhiteSpace(matchId) || !session.CompetitionTransportService.IsConnected)
        {
            return;
        }
        StartAckState state = success ? StartAckState.Success : StartAckState.Failure;
        if (_localAckStates.ContainsKey(matchId))
        {
            MainFile.Logger.Info($"Duplicate start ACK ignored: {matchId}.");
            return;
        }
        _localAckStates[matchId] = state;
        MainFile.Logger.Info($"Sending final start ACK: {state.ToString().ToLowerInvariant()}.");
        session.CompetitionTransportService.SendMessage(new CompetitionStartAckMessage { MatchId = matchId, PlayerId = session.CompetitionTransportService.NetId, Success = success, Error = error });
        MainFile.Logger.Info($"Start ACK finalized: {state.ToString().ToLowerInvariant()}, match={matchId}.");
    }

    private static async Task StartCompetitionRunAsync(CompetitionMatchSession session, string matchId, string seed, int ascension, CharacterModel character, IReadOnlyList<ActModel> acts, IReadOnlyList<ModifierModel> modifiers)
    {
        if (!ReferenceEquals(_session, session) || session.IsInRun || NGame.Instance == null)
            throw new InvalidOperationException("Competition match session is no longer valid.");

        MainFile.Logger.Info("Entering Competition run context.");
        _originalRunSnapshot = OriginalRunSnapshot.Capture();
        MainFile.Logger.Info($"Original singleplayer run present: {_originalRunSnapshot.Exists.ToString().ToLowerInvariant()}.");
        MainFile.Logger.Info("Original run preservation enabled.");
        CompetitionRunContext.Enter(matchId);
        try
        {
            // From this point through MarkInRun is phase A: only failure here
            // is a genuine start failure. UI callbacks run strictly afterwards.
            CompetitionHostLobbyScreen.DetachForCompetitionRun();
            MainFile.Logger.Info("Starting isolated Competition run.");
            MainFile.Logger.Info("Preserving network session during scene transition.");
            LogPreRunNetworkDiagnostics(session);
            RunState runState = await NGame.Instance.StartNewSingleplayerRun(character, shouldSave: false, acts, modifiers, seed, GameMode.Standard, ascension);
            if (!IsIsolatedSingleplayerRun(session, runState))
                throw new InvalidOperationException("Competition run was not initialized as an independent singleplayer run.");

            LogVerifiedRunIsolation(session, runState);

            session.MarkInRun();
            MainFile.Logger.Info("Competition run creation completed.");
            MainFile.Logger.Info($"Run state committed: match={matchId}.");
            MainFile.Logger.Info("Session state: IsStarting=false, IsInRun=true.");
            MainFile.Logger.Info("Competition context remains active.");
        }
        catch
        {
            if (!session.IsInRun) CompetitionRunContext.Exit("core run creation failed");
            throw;
        }
    }

    private static void NotifyPostStart(CompetitionMatchSession session)
    {
        // Phase B: nothing here is allowed to change an already committed run.
        MainFile.Logger.Info("Competition transport remains connected.");
        MainFile.Logger.Info($"Network service still connected: {session.CompetitionTransportService.IsConnected.ToString().ToLowerInvariant()}.");
        MainFile.Logger.Info("Original singleplayer run remains untouched.");
        _originalRunSnapshot?.LogUnchanged();
        NotifyStateChanged();
    }

    private static IReadOnlyList<ActModel> ResolveActs(string ids) => ids.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(id => ModelDb.GetById<ActModel>(ModelId.Deserialize(id))).ToList();
    private static IReadOnlyList<ModifierModel> ResolveModifiers(string ids) => string.IsNullOrWhiteSpace(ids) ? Array.Empty<ModifierModel>() : ids.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(id => ModelDb.GetById<ModifierModel>(ModelId.Deserialize(id))).ToList();

    private static void HandleStartFailure(CompetitionMatchSession session, Exception exception)
    {
        if (session.IsInRun)
        {
            MainFile.Logger.Warn("Ignoring late start failure because the run is already active.");
            return;
        }
        MainFile.Logger.Error($"Match start failed: {exception}");
        CompetitionRunContext.Exit("core match start failure");
        MainFile.Logger.Info("Original singleplayer run remains untouched.");
        _originalRunSnapshot?.LogUnchanged();
        session.ResetAfterFailedStart();
        MainFile.Logger.Info("Returning to lobby after failed start.");
        NotifyStateChanged();
    }

    private static void NotifyStateChanged() => StateChanged?.Invoke();
    private static ulong GetHostPlayerId(CompetitionMatchSession session) => session.Lobby.Players.First(player => player.slotId == 0).id;

    private static void LogPreRunNetworkDiagnostics(CompetitionMatchSession session)
    {
        string runService = RunManager.Instance.IsInProgress ? GetServiceType(RunManager.Instance.NetService) : "null";
        MainFile.Logger.Info(
            "Pre-run network diagnostics: " +
            $"lobbyService={GetServiceType(session.CompetitionTransportService)}, " +
            $"lobbyConnected={session.CompetitionTransportService.IsConnected.ToString().ToLowerInvariant()}, " +
            $"runService={runService}, globalService=null, gameMode={GameMode.Standard}, " +
            "playerCount=0, isMultiplayer=false.");
        MainFile.Logger.Info($"Competition transport service: {GetServiceType(session.CompetitionTransportService)}.");
    }

    private static bool IsIsolatedSingleplayerRun(CompetitionMatchSession session, RunState runState)
    {
        return RunManager.Instance.IsInProgress &&
               RunManager.Instance.NetService.Type == NetGameType.Singleplayer &&
               RunManager.Instance.NetService is NetSingleplayerGameService &&
               RunManager.Instance.RunLobby == null &&
               runState.Players.Count == 1 &&
               !ReferenceEquals(session.CompetitionTransportService, RunManager.Instance.NetService);
    }

    private static void LogVerifiedRunIsolation(CompetitionMatchSession session, RunState runState)
    {
        INetGameService runService = RunManager.Instance.NetService;
        bool runIsMultiplayer = runService.Type.IsMultiplayer();
        MainFile.Logger.Info(
            "Post-run network diagnostics: " +
            $"transportService={GetServiceType(session.CompetitionTransportService)}, " +
            $"runService={GetServiceType(runService)}, runGameMode={runState.GameMode}, " +
            $"runPlayerCount={runState.Players.Count}, runIsMultiplayer={runIsMultiplayer.ToString().ToLowerInvariant()}.");
        MainFile.Logger.Info($"Local run network service: {GetServiceType(runService)}.");
        MainFile.Logger.Info($"Local run multiplayer flag: {runIsMultiplayer.ToString().ToLowerInvariant()}.");
        MainFile.Logger.Info($"Local run player count: {runState.Players.Count}.");
        MainFile.Logger.Info("Competition transport separated from local run.");
        MainFile.Logger.Info("Local run has no multiplayer NetService.");
        // v0.107.1 creates the synchronizer objects for every RunManager, but
        // a NetSingleplayerGameService has no handler or send implementation.
        // Their multiplayer code paths are therefore inactive without a patch.
        MainFile.Logger.Info("Vanilla multiplayer run synchronization inactive.");
    }

    private static string GetServiceType(INetGameService? service) => service?.GetType().FullName ?? "null";

    private sealed record OriginalRunSnapshot(string Path, bool Exists, long Length, DateTime LastWriteTimeUtc)
    {
        public static OriginalRunSnapshot Capture()
        {
            string path = RunSaveManager.GetRunSavePath(SaveManager.Instance.CurrentProfileId, "current_run.save");
            FileInfo info = new(path);
            return new OriginalRunSnapshot(path, info.Exists, info.Exists ? info.Length : 0, info.Exists ? info.LastWriteTimeUtc : DateTime.MinValue);
        }
        public void LogUnchanged()
        {
            FileInfo info = new(Path);
            bool unchanged = Exists == info.Exists && (!Exists || Length == info.Length && LastWriteTimeUtc == info.LastWriteTimeUtc);
            MainFile.Logger.Info($"Original singleplayer run remains untouched: {unchanged.ToString().ToLowerInvariant()}.");
        }
    }
}
