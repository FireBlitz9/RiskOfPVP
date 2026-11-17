using System;
using System.Collections.Generic;
using System.Linq;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace ArenaPvP
{
    [Serializable]
    internal sealed class MapView
    {
        public int Index, Votes;
        public string Name, InternalName;
    }
    [Serializable]
    internal sealed class LobbyView
    {
        public uint UserId;
        public int VotedMap = -1;
        public int SelectedMap = -1;
        public int VotedMode = -1, SelectedMode;
        public int[] ModeVotes = new int[3];
        public MapView[] Maps = new MapView[0];
    }
    internal sealed class VoteMessage : MessageBase
    {
        internal uint UserId;
        internal int Map;
        internal bool Mode;
        public override void Serialize(NetworkWriter writer) { writer.Write(UserId); writer.Write(Map); writer.Write(Mode); }
        public override void Deserialize(NetworkReader reader) { UserId = reader.ReadUInt32(); Map = reader.ReadInt32(); Mode = reader.ReadBoolean(); }
    }

    internal sealed class ArenaLobby : MonoBehaviour
    {
        internal static ArenaLobby Instance;
        internal static readonly Dictionary<uint, LobbyView> Views = new Dictionary<uint, LobbyView>();
        internal const short VoteId = 28674, LobbyId = 28675;
        private readonly Dictionary<NetworkUserId, int> votes = new Dictionary<NetworkUserId, int>();
        private readonly Dictionary<NetworkUserId, int> modeVotes = new Dictionary<NetworkUserId, int>();
        private ArenaMode? lockedMode;
        private readonly Dictionary<NetworkUserId, float> lastVotes = new Dictionary<NetworkUserId, float>();
        private readonly List<int> eligible = new List<int>();
        private float nextUpdate;
        private bool previouslySelected;
        private SceneDef lockedMap;
        private bool mapCacheValid;
        private string cachedBlacklist;
        private int cachedSceneCount;
        private RuleBook cachedBook;
        private MapView[] mapDefinitions = new MapView[0];

        internal static bool Selected
        {
            get { return Plugin.ArenaDifficulty != DifficultyIndex.Invalid && PreGameController.instance
                && PreGameController.instance.readOnlyRuleBook.FindDifficulty() == Plugin.ArenaDifficulty; }
        }

        private void Awake() { Instance = this; Language.onCurrentLanguageChanged += InvalidateMaps; }
        private void OnDestroy() { Language.onCurrentLanguageChanged -= InvalidateMaps; Instance = null; Views.Clear(); }
        private void InvalidateMaps() { mapCacheValid = false; }
        internal void ResetAfterRun() { votes.Clear(); modeVotes.Clear(); lockedMode = null; lastVotes.Clear(); eligible.Clear(); lockedMap = null; Views.Clear(); mapCacheValid = false; mapDefinitions = new MapView[0]; ArenaHostSettings.Clear(); }

        private void Update()
        {
            if (!NetworkServer.active || Time.unscaledTime < nextUpdate) return;
            nextUpdate = Time.unscaledTime + 0.4f;
            if (Selected)
            {
                if (!previouslySelected) ResetAfterRun();
                previouslySelected = true;
                if (!PreGameController.instance.IsCharacterSwitchingCurrentlyAllowed()) return;
                RefreshMaps(); SendViews();
            }
            else if (PreGameController.instance)
            {
                if (previouslySelected) ResetAfterRun();
                previouslySelected = false;
            }
            // Preserve the locked ballot while the lobby is being replaced by the Run.
        }

        internal void RefreshMaps()
        {
            var book = PreGameController.instance ? PreGameController.instance.readOnlyRuleBook : (Run.instance ? Run.instance.ruleBook : null);
            string names = Plugin.Settings.MapBlacklist.Value;
            if (mapCacheValid && cachedBlacklist == names && cachedSceneCount == SceneCatalog.sceneDefCount && book != null && cachedBook.Equals(book)) return;
            eligible.Clear();
            var blacklist = new HashSet<string>(names.Split(',').Select(x => x.Trim()), StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < SceneCatalog.sceneDefCount; i++)
            {
                var scene = SceneCatalog.GetSceneDef((SceneIndex)i);
                if (!ArenaStages.Playable(scene)) continue;
                if (blacklist.Contains(scene.cachedName) || blacklist.Contains(scene.baseSceneName) || blacklist.Contains(Language.GetString(scene.nameToken))) continue;
                if (scene.requiredExpansion && (book == null || scene.requiredExpansion.enabledChoice == null || !book.IsChoiceActive(scene.requiredExpansion.enabledChoice))) continue;
                eligible.Add(i);
            }
            foreach (var id in votes.Keys.ToArray()) if (!eligible.Contains(votes[id])) votes.Remove(id);
            mapDefinitions = eligible.Select(i => new MapView {
                Index = i, InternalName = SceneCatalog.GetSceneDef((SceneIndex)i).cachedName,
                Name = Language.GetString(SceneCatalog.GetSceneDef((SceneIndex)i).nameToken)
            }).OrderBy(m => m.Name).ThenBy(m => m.InternalName).ToArray();
            if (book != null) { if (cachedBook == null) cachedBook = new RuleBook(); cachedBook.Copy(book); }
            cachedBlacklist = names; cachedSceneCount = SceneCatalog.sceneDefCount; mapCacheValid = book != null;
        }

        internal bool HasMaps()
        { RefreshMaps(); return eligible.Count > 0; }

        internal SceneDef LockWinner()
        {
            if (lockedMap) return lockedMap;
            RefreshMaps();
            var connected = new HashSet<NetworkUserId>(NetworkUser.readOnlyInstancesList.Where(ArenaNetwork.IsUserPresent).Select(u => u.id));
            int winner = MapVotingRules.Preview(eligible.ToArray(), votes.Where(v => connected.Contains(v.Key)).Select(v => v.Value).ToArray(), DefaultMapIndex());
            if (winner < 0) return null;
            lockedMap = SceneCatalog.GetSceneDef((SceneIndex)winner);
            Plugin.Log("Arena map vote selected " + lockedMap.cachedName + ".");
            return lockedMap;
        }

        internal ArenaMode LockMode()
        { if (!lockedMode.HasValue) lockedMode = (ArenaMode)PreviewMode(); return lockedMode.Value; }

        private int PreviewMode()
        {
            var present = new HashSet<NetworkUserId>(NetworkUser.readOnlyInstancesList.Where(ArenaNetwork.IsUserPresent).Select(u => u.id));
            return MapVotingRules.Preview(new[] { 0, 1, 2 }, modeVotes.Where(v => present.Contains(v.Key)).Select(v => v.Value).ToArray(), (int)Plugin.Settings.DefaultMode.Value);
        }

        internal void Vote(NetworkUser user, int index, bool mode = false)
        {
            if (!Selected || !NetworkServer.active || !ArenaNetwork.IsUserPresent(user)) return;
            RefreshMaps();
            if (lockedMode.HasValue || (mode ? index < 0 || index > 2 : !eligible.Contains(index))) return;
            float last;
            if (lastVotes.TryGetValue(user.id, out last) && Time.unscaledTime - last < 0.15f) return;
            lastVotes[user.id] = Time.unscaledTime;
            (mode ? modeVotes : votes)[user.id] = index; // Independent map and mode ballots.
            SendViews();
        }

        private void SendViews()
        {
            var connected = NetworkUser.readOnlyInstancesList.Where(ArenaNetwork.IsUserPresent).ToArray();
            var identities = new HashSet<NetworkUserId>(connected.Select(u => u.id));
            int mode = PreviewMode(); var modeCounts = new int[3];
            foreach (var vote in modeVotes) if (identities.Contains(vote.Key)) modeCounts[vote.Value]++;
            int selectedMap = MapVotingRules.Preview(eligible.ToArray(), votes.Where(v => identities.Contains(v.Key)).Select(v => v.Value).ToArray(), DefaultMapIndex());
            var counts = new Dictionary<int, int>();
            foreach (var vote in votes) if (identities.Contains(vote.Key))
            { int count; counts.TryGetValue(vote.Value, out count); counts[vote.Value] = count + 1; }
            var maps = mapDefinitions.Select(definition => new MapView
            {
                Index = definition.Index, InternalName = definition.InternalName, Name = definition.Name,
                Votes = counts.ContainsKey(definition.Index) ? counts[definition.Index] : 0
            }).ToArray();
            foreach (var user in connected)
            {
                int ballot;
                var view = new LobbyView { UserId = user.netId.Value, VotedMap = votes.TryGetValue(user.id, out ballot) ? ballot : -1, SelectedMap = selectedMap, Maps = maps,
                    SelectedMode = mode, VotedMode = modeVotes.TryGetValue(user.id, out ballot) ? ballot : -1, ModeVotes = modeCounts };
                if (user.localUser != null) Views[user.netId.Value] = view;
                else user.connectionToClient.Send(LobbyId, new SnapshotMessage { Json = JsonUtility.ToJson(view) });
            }
        }

        private int DefaultMapIndex()
        {
            string requested = (Plugin.Settings.DefaultMap.Value ?? "").Trim();
            foreach (int index in eligible)
            {
                var scene = SceneCatalog.GetSceneDef((SceneIndex)index);
                if (string.Equals(requested, scene.cachedName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(requested, scene.baseSceneName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(requested, Language.GetString(scene.nameToken), StringComparison.OrdinalIgnoreCase)) return index;
            }
            return eligible.Count == 0 ? -1 : eligible[0];
        }

        internal static void ReceiveVote(NetworkMessage message)
        {
            if (!Selected || !NetworkServer.active || !Instance) return;
            try
            {
                var request = message.ReadMessage<VoteMessage>();
                var user = NetworkUser.readOnlyInstancesList.FirstOrDefault(u => u && u.netId.Value == request.UserId);
                if (user && user.connectionToClient == message.conn) Instance.Vote(user, request.Map, request.Mode);
            }
            catch (Exception) { }
        }
        internal static void ReceiveView(NetworkMessage message)
        {
            if (!Selected) return;
            try
            {
                var view = JsonUtility.FromJson<LobbyView>(message.ReadMessage<SnapshotMessage>().Json);
                if (view != null && view.Maps != null && view.Maps.Length <= 512 && view.ModeVotes != null && view.ModeVotes.Length == 3) Views[view.UserId] = view;
            }
            catch (Exception) { }
        }
        internal static void Submit(NetworkUser user, int map, bool mode = false)
        {
            if (!Selected || !user || user.localUser == null) return;
            if (NetworkServer.active) { if (Instance) Instance.Vote(user, map, mode); return; }
            var client = NetworkClient.allClients.FirstOrDefault(c => c != null && c.isConnected);
            if (client != null) client.Send(VoteId, new VoteMessage { UserId = user.netId.Value, Map = map, Mode = mode });
        }
    }
}
