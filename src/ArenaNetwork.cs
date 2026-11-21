using System;
using System.Collections.Generic;
using System.Linq;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace ArenaPvP
{
    [Serializable]
    internal sealed class PlayerView
    {
        public uint UserId;
        public uint MasterId;
        public bool IsBot;
        public string Name;
        public int Kills, Deaths, Level, Side = -1;
        public bool Dead, Online;
        public float Protection;
    }

    [Serializable]
    internal sealed class MatchView
    {
        public uint UserId, OfferId, RewardRevision, RunId;
        public uint ContinueRound;
        public int ContinueVotes, ContinueVoters, ContinueRequired;
        public bool CanVoteContinue, HasContinueVote, ReturningToLobby;
        public int PlayAgainVotes;
        public bool HasPlayAgainVote, Restarting;
        public float Elapsed, Warmup, NextReward, NextCap, Respawn;
        public int Cap, MaximumCap, Pending, Selection, KillLimit;
        public int ScrapTier = -1;
        public int Mode, OwnSide = -1, RedKills, BlueKills;
        public HillView Hill;
        public float TimeLimit;
        public bool Finished, LevelEnabled, CanLevel, WaitingChoice, TimerEnabled, NoFallDamage, ScrapperEnabled, VoidProtection;
        public string Result;
        public int[] Items;
        public PlayerView[] Players;
    }

    internal sealed class SnapshotMessage : MessageBase
    {
        internal string Json;
        public override void Serialize(NetworkWriter writer) { writer.Write(Json); }
        public override void Deserialize(NetworkReader reader) { Json = reader.ReadString(); }
    }

    [Serializable]
    internal sealed class RosterView { public uint RunId; public PlayerView[] Players; }

    internal sealed class MatchSnapshotMessage : MessageBase
    {
        internal string Json;
        private byte[] encoded;
        public override void Serialize(NetworkWriter writer)
        { if (encoded == null) encoded = MatchSnapshotCodec.Encode(Json); writer.WriteBytesFull(encoded); }
        public override void Deserialize(NetworkReader reader) { Json = MatchSnapshotCodec.Decode(reader.ReadBytesAndSize()); }
    }

    internal sealed class ChooseMessage : MessageBase
    {
        internal uint UserId, OfferId;
        internal int Selection;
        public override void Serialize(NetworkWriter writer)
        { writer.Write(UserId); writer.Write(OfferId); writer.Write(Selection); }
        public override void Deserialize(NetworkReader reader)
        { UserId = reader.ReadUInt32(); OfferId = reader.ReadUInt32(); Selection = reader.ReadInt32(); }
    }

    internal sealed class ContinueVoteMessage : MessageBase
    {
        internal uint UserId, Round;
        internal bool Support;
        internal bool PlayAgain;
        public override void Serialize(NetworkWriter writer) { writer.Write(UserId); writer.Write(Round); writer.Write(Support); writer.Write(PlayAgain); }
        public override void Deserialize(NetworkReader reader) { UserId = reader.ReadUInt32(); Round = reader.ReadUInt32(); Support = reader.ReadBoolean(); PlayAgain = reader.ReadBoolean(); }
    }

    internal static class ArenaNetwork
    {
        // Private protocol message IDs; both peers must have this exact mod version.
        internal const short ChooseId = 28672, SnapshotId = 28673, ContinueVoteId = 28678, RosterId = 28679;
        internal static readonly Dictionary<uint, MatchView> Views = new Dictionary<uint, MatchView>();
        private static readonly HashSet<NetworkClient> registeredClients = new HashSet<NetworkClient>();
        private static bool serverRegistered;
        private static PlayerView[] roster = new PlayerView[0];
        private static readonly Dictionary<uint, PlayerView> botRoster = new Dictionary<uint, PlayerView>();
        internal static bool TryFindBot(uint masterId, out PlayerView bot) => botRoster.TryGetValue(masterId, out bot);

        // A listen-server host is a participant even without a remote client
        // connection. This also applies while running alone in a multiplayer lobby.
        internal static bool IsUserPresent(NetworkUser user)
        { return user && user.isActiveAndEnabled && ArenaRules.IsUserPresent(NetworkServer.active,
            user.localUser != null, user.connectionToClient != null); }

        internal static bool IsHostUser(NetworkUser user)
        { return NetworkServer.active && user && user.localUser != null && IsUserPresent(user); }

        internal static void EnsureHandlers()
        {
            if (NetworkServer.active && !serverRegistered)
            { NetworkServer.RegisterHandler(ChooseId, OnChoose); NetworkServer.RegisterHandler(ContinueVoteId, OnContinueVote); NetworkServer.RegisterHandler(ArenaLobby.VoteId, ArenaLobby.ReceiveVote);
                NetworkServer.RegisterHandler(ArenaHostSettings.RequestId, ArenaHostSettings.ReceiveRequest); serverRegistered = true; }
            if (!NetworkServer.active) serverRegistered = false;
            foreach (var client in NetworkClient.allClients)
                if (client != null && registeredClients.Add(client))
                { client.RegisterHandler(SnapshotId, OnSnapshot); client.RegisterHandler(ArenaLobby.LobbyId, ArenaLobby.ReceiveView);
                    client.RegisterHandler(RosterId, OnRoster);
                    client.RegisterHandler(ArenaHostSettings.ViewId, ArenaHostSettings.ReceiveView); }
            registeredClients.RemoveWhere(c => !NetworkClient.allClients.Contains(c));
        }

        private static void OnChoose(NetworkMessage message)
        {
            if (!NetworkServer.active || !Plugin.Active || !ArenaMatch.Instance || message.conn == null) return;
            try
            {
                var request = message.ReadMessage<ChooseMessage>();
                var user = NetworkUser.readOnlyInstancesList.FirstOrDefault(u => u && u.netId.Value == request.UserId);
                // Never accept a client-supplied master or trust the requested user's identity.
                if (!user || user.connectionToClient != message.conn) return;
                ArenaMatch.Instance.Choose(user, request.OfferId, request.Selection);
            }
            catch (Exception) { /* Malformed client packets do not alter match state. */ }
        }

        private static void OnSnapshot(NetworkMessage message)
        {
            if (!Plugin.Active) return;
            try
            {
                var view = JsonUtility.FromJson<MatchView>(message.ReadMessage<MatchSnapshotMessage>().Json);
                if (view != null && view.RunId == Run.instance.netId.Value && view.Items != null && view.Items.Length <= 2048)
                { view.Players = roster; Views[view.UserId] = view; }
            }
            catch (Exception) { }
        }

        private static void OnRoster(NetworkMessage message)
        {
            if (!Plugin.Active) return;
            try
            {
                var view = JsonUtility.FromJson<RosterView>(message.ReadMessage<MatchSnapshotMessage>().Json);
                if (view == null || view.RunId != Run.instance.netId.Value || view.Players == null) return;
                roster = view.Players;
                botRoster.Clear();
                foreach (var player in roster)
                    if (player != null && player.IsBot && player.Online && player.MasterId != 0)
                        botRoster[player.MasterId] = player;
                foreach (var match in Views.Values) match.Players = roster;
            }
            catch (Exception) { }
        }

        private static void OnContinueVote(NetworkMessage message)
        {
            if (!NetworkServer.active || !Plugin.Active || !ArenaMatch.Instance || message.conn == null) return;
            try
            {
                var request = message.ReadMessage<ContinueVoteMessage>();
                var user = NetworkUser.readOnlyInstancesList.FirstOrDefault(u => u && u.netId.Value == request.UserId);
                if (!user || user.connectionToClient != message.conn) return;
                ArenaMatch.Instance.VoteContinue(user, request.Round, request.Support, request.PlayAgain);
            }
            catch (Exception) { /* Invalid packets cannot change the ballot. */ }
        }

        internal static void VoteContinue(NetworkUser user, uint round, bool support, bool playAgain = false)
        {
            if (!user || user.localUser == null || !Plugin.Active) return;
            if (NetworkServer.active) { if (ArenaMatch.Instance) ArenaMatch.Instance.VoteContinue(user, round, support, playAgain); return; }
            var client = NetworkClient.allClients.FirstOrDefault(c => c != null && c.isConnected);
            if (client != null) client.Send(ContinueVoteId, new ContinueVoteMessage { UserId = user.netId.Value, Round = round, Support = support, PlayAgain = playAgain });
        }

        internal static void Choose(NetworkUser user, uint offerId, int item)
        {
            if (!user || user.localUser == null || !Plugin.Active) return;
            if (NetworkServer.active) { if (ArenaMatch.Instance) ArenaMatch.Instance.Choose(user, offerId, item); return; }
            var client = NetworkClient.allClients.FirstOrDefault(c => c != null && c.isConnected);
            if (client != null) client.Send(ChooseId, new ChooseMessage { UserId = user.netId.Value, OfferId = offerId, Selection = item });
        }

        internal static void SendSnapshots()
        {
            var match = ArenaMatch.Instance;
            if (!NetworkServer.active || !match || match.Rules == null) return;
            // Build/serialize the shared roster once per batch, independently
            // from each player's private reward choices and ballot state.
            var players = match.AllPlayers.Select(other => new PlayerView
            {
                UserId = other.UserId, Name = ArenaMatch.SafeName(other.Name), Kills = other.Kills,
                MasterId = other.Master ? other.Master.netId.Value : 0, IsBot = other.IsBot,
                Deaths = other.Deaths, Level = other.Level, Dead = other.Dead, Online = other.Connected, Side = other.Side,
                Protection = Math.Max(0f, other.ProtectionUntil - match.Clock)
            }).ToArray();
            NetworkServer.SendToAll(RosterId, new MatchSnapshotMessage { Json = JsonUtility.ToJson(new RosterView { RunId = Run.instance.netId.Value, Players = players }) });
            foreach (var p in match.Players.Values.Where(p => p.Connected))
            {
                var offer = p.Rewards.First;
                var rules = match.Rules;
                float capInterval = rules.Selection == ItemSelection.Random ? rules.RandomLevelSeconds : rules.CapInterval;
                var view = new MatchView
                {
                    UserId = p.UserId, RunId = Run.instance.netId.Value, Elapsed = match.Elapsed, Warmup = match.WarmupRemaining,
                    ContinueRound = match.ContinueVotes.Round, ContinueVotes = match.ContinueVotes.VoteCount,
                    ContinueVoters = match.ContinueVotes.EligibleCount, ContinueRequired = match.ContinueVotes.Required,
                    CanVoteContinue = match.Finished && !match.ReturningToLobby && match.ContinueVotes.CanVote(p.User.id),
                    HasContinueVote = match.ContinueVotes.HasVoted(p.User.id), ReturningToLobby = match.ReturningToLobby,
                    PlayAgainVotes = match.ContinueVotes.RestartVoteCount, HasPlayAgainVote = match.ContinueVotes.HasRestartVoted(p.User.id), Restarting = match.Restarting,
                    NextReward = Math.Max(0f, match.NextReward - match.Elapsed),
                    NextCap = match.Cap >= rules.MaximumLevel ? 0f : capInterval - match.Elapsed % capInterval,
                    Cap = match.Cap, MaximumCap = rules.MaximumLevel, KillLimit = rules.KillLimit,
                    TimeLimit = match.TimeLimit, TimerEnabled = rules.OnTimer, ScrapperEnabled = rules.EnableScrapper,
                    Respawn = p.Dead ? Math.Max(0f, p.RespawnAt - match.Clock) : 0f,
                    WaitingChoice = p.Dead && p.ChoiceRequired && !p.ChoiceComplete,
                    Pending = p.PendingRewards, Selection = (int)rules.Selection, Finished = match.Finished,
                    Result = match.Result, OfferId = offer == null ? 0 : offer.Id,
                    RewardRevision = p.Rewards.ArrivalRevision,
                    Items = offer == null ? new int[0] : match.AvailableChoices(p, offer.Items),
                    ScrapTier = offer == null ? -1 : offer.ScrapTier,
                    Mode = (int)rules.Mode, OwnSide = p.Side, RedKills = match.TeamKills[0], BlueKills = match.TeamKills[1],
                    Hill = rules.Mode == ArenaMode.KOTH ? match.Hill.View(rules) : null,
                    LevelEnabled = rules.AllowLevel && rules.Selection != ItemSelection.Random,
                    CanLevel = rules.AllowLevel && p.Level < match.Cap, NoFallDamage = rules.NoFallDamage, VoidProtection = rules.VoidProtection,
                    Players = players
                };
                if (p.User.localUser != null) Views[p.UserId] = view;
                else
                {
                    view.Players = new PlayerView[0];
                    p.User.connectionToClient.Send(SnapshotId, new MatchSnapshotMessage { Json = JsonUtility.ToJson(view) });
                }
            }
        }

        internal static void ClearViews() { Views.Clear(); roster = new PlayerView[0]; botRoster.Clear(); ArenaHostSettings.Clear(); }
        internal static void Shutdown()
        {
            if (NetworkServer.active && serverRegistered) { NetworkServer.UnregisterHandler(ChooseId); NetworkServer.UnregisterHandler(ContinueVoteId); NetworkServer.UnregisterHandler(ArenaLobby.VoteId); NetworkServer.UnregisterHandler(ArenaHostSettings.RequestId); }
            foreach (var c in registeredClients) if (c != null) { c.UnregisterHandler(SnapshotId); c.UnregisterHandler(RosterId); c.UnregisterHandler(ArenaLobby.LobbyId); c.UnregisterHandler(ArenaHostSettings.ViewId); }
            registeredClients.Clear(); serverRegistered = false; Views.Clear();
            ArenaHostSettings.Clear();
            roster = new PlayerView[0];
            botRoster.Clear();
        }
    }
}
