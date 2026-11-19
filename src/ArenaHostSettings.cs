using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BepInEx.Configuration;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace ArenaPvP
{
    [Serializable]
    internal sealed class HostRuleView
    {
        public string Group, Name, Description, Value;
        public bool Enabled;
    }
    [Serializable]
    internal sealed class HostSettingsView
    {
        public uint Revision;
        public HostRuleView[] Rules;
    }
    // Read requests carry no setting keys or values. Remote users cannot submit edits.
    internal sealed class HostSettingsRequest : MessageBase
    {
        internal uint UserId, KnownRevision;
        public override void Serialize(NetworkWriter writer) { writer.Write(UserId); writer.Write(KnownRevision); }
        public override void Deserialize(NetworkReader reader) { UserId = reader.ReadUInt32(); KnownRevision = reader.ReadUInt32(); }
    }
    internal static class ArenaHostSettings
    {
        internal const short RequestId = 28676, ViewId = 28677;
        internal static HostSettingsView ClientView;
        private static readonly Dictionary<uint, float> lastRequests = new Dictionary<uint, float>();
        private static uint cachedRevision;
        private static string cachedJson;

        internal static string Id(ConfigEntryBase entry) { return entry.Definition.Section + "/" + entry.Definition.Key; }
        internal static string Value(ConfigEntryBase entry)
        { return Convert.ToString(entry.BoxedValue, CultureInfo.InvariantCulture) ?? ""; }
        internal static HostSettingsView Capture()
        {
            return new HostSettingsView { Revision = Plugin.Settings.MatchRevision,
                Rules = Plugin.Settings.MatchEntries.Select(e => new HostRuleView {
                    Group = e.Definition.Section, Name = e.Definition.Key,
                    Description = e.Description.Description, Value = Value(e), Enabled = Plugin.Settings.IsEnabled(e)
                }).ToArray() };
        }
        internal static bool TryEdit(ConfigEntryBase entry, string value)
        {
            if (!NetworkServer.active || !ArenaLobby.Selected || entry == null || !Plugin.Settings.MatchEntries.Contains(entry)) return false;
            try
            {
                object parsed;
                if (entry.SettingType == typeof(bool)) parsed = bool.Parse(value);
                else if (entry.SettingType == typeof(int)) parsed = int.Parse(value.Trim().TrimEnd('%'), NumberStyles.Integer, CultureInfo.InvariantCulture);
                else if (entry.SettingType == typeof(float))
                {
                    float number = float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
                    if (float.IsNaN(number) || float.IsInfinity(number)) return false;
                    parsed = number;
                }
                else if (entry.SettingType.IsEnum) parsed = Enum.Parse(entry.SettingType, value);
                else if (entry.SettingType == typeof(string)) parsed = value;
                else return false;
                entry.BoxedValue = parsed;
                return true;
            }
            catch (FormatException) { return false; }
            catch (OverflowException) { return false; }
            catch (ArgumentException) { return false; }
        }

        internal static void Request(NetworkUser user)
        {
            if (!ArenaLobby.Selected || NetworkServer.active || !user || user.localUser == null) return;
            var client = NetworkClient.allClients.FirstOrDefault(c => c != null && c.isConnected);
            if (client != null) client.Send(RequestId, new HostSettingsRequest { UserId = user.netId.Value,
                KnownRevision = ClientView == null ? 0 : ClientView.Revision });
        }
        internal static void ReceiveRequest(NetworkMessage message)
        {
            if (!NetworkServer.active || !ArenaLobby.Selected || message.conn == null) return;
            try
            {
                var request = message.ReadMessage<HostSettingsRequest>();
                var user = NetworkUser.readOnlyInstancesList.FirstOrDefault(u => u && u.netId.Value == request.UserId);
                if (!user || user.connectionToClient != message.conn) return;
                float previous;
                if (lastRequests.TryGetValue(request.UserId, out previous) && Time.unscaledTime - previous < 0.25f) return;
                lastRequests[request.UserId] = Time.unscaledTime;
                if (request.KnownRevision == Plugin.Settings.MatchRevision) return;
                if (cachedJson == null || cachedRevision != Plugin.Settings.MatchRevision)
                { cachedJson = SettingsSnapshotCodec.Encode(JsonUtility.ToJson(Capture())); cachedRevision = Plugin.Settings.MatchRevision; }
                if (cachedJson.Length < 60000) message.conn.Send(ViewId, new SnapshotMessage { Json = cachedJson });
            }
            catch (Exception error) { Plugin.Warn("Host settings read request was ignored: " + error.Message); }
        }
        internal static void ReceiveView(NetworkMessage message)
        {
            if (NetworkServer.active || !ArenaLobby.Selected) return;
            try
            {
                string json = message.ReadMessage<SnapshotMessage>().Json;
                if (string.IsNullOrEmpty(json) || json.Length > 60000) return;
                var view = JsonUtility.FromJson<HostSettingsView>(SettingsSnapshotCodec.Decode(json));
                if (view == null || view.Rules == null || view.Rules.Length > 256) return;
                if (view.Rules.Any(r => r == null || !ArenaRules.IsMatchSettingGroup(r.Group)
                    || string.IsNullOrEmpty(r.Name) || r.Name.Length > 128 || (r.Value ?? "").Length > 262144
                    || (r.Description ?? "").Length > 4096)) return;
                ClientView = view;
            }
            catch (Exception) { }
        }
        internal static void Clear()
        { ClientView = null; cachedJson = null; cachedRevision = 0; lastRequests.Clear(); }
    }
}
